#import "XYWeChatBridge.h"
#import "XYShareCorrelation.h"
#if __has_include(<WechatOpenSDK/WXApi.h>)
#import <WechatOpenSDK/WXApi.h>
#define XY_HAS_SDK 1
@interface XYWeChatBridge () <WXApiDelegate>
#else
#define XY_HAS_SDK 0
@interface XYWeChatBridge ()
#endif
@property(nonatomic, readwrite) BOOL configured;
@property(nonatomic, strong) XYShareCorrelation *correlation;
@property(nonatomic, strong, nullable) NSTimer *shareTimer;
@property(nonatomic, copy) NSString *appID;
@property(nonatomic, copy) NSURL *universalLink;
@end

@implementation XYWeChatBridge
- (instancetype)init {
    self = [super init];
    if (self) _correlation = [[XYShareCorrelation alloc] init];
    return self;
}
- (NSString *)pendingRequestID { return self.correlation.pendingRequestID; }
- (BOOL)sdkAvailable { return XY_HAS_SDK; }
- (BOOL)configureAppID:(NSString *)appID universalLink:(NSString *)universalLink {
    self.appID = appID;
    self.universalLink = [NSURL URLWithString:universalLink];
#if XY_HAS_SDK
    self.configured = appID.length > 0 && self.universalLink != nil && [WXApi registerApp:appID universalLink:universalLink];
#else
    self.configured = NO;
#endif
    return self.configured;
}
- (BOOL)isWeChatInstalled {
#if XY_HAS_SDK
    return [WXApi isWXAppInstalled];
#else
    return NO;
#endif
}
- (void)shareText:(NSString *)text requestID:(NSString *)requestID completion:(void (^)(BOOL))completion {
    if (!self.configured || self.pendingRequestID || text.length == 0 || [text lengthOfBytesUsingEncoding:NSUTF8StringEncoding] > 10240) { completion(NO); return; }
#if XY_HAS_SDK
    if (![self.correlation beginRequest:requestID]) { completion(NO); return; }
    [self.shareTimer invalidate];
    __weak XYWeChatBridge *weakSelf = self;
    self.shareTimer = [NSTimer scheduledTimerWithTimeInterval:120 repeats:NO block:^(NSTimer *timer) {
        XYWeChatBridge *strongSelf = weakSelf;
        if (![strongSelf.correlation expireRequest:requestID]) return;
        strongSelf.shareTimer = nil;
        if (strongSelf.resultHandler) strongSelf.resultHandler(requestID, @"error", @"result_unknown");
    }];
    SendMessageToWXReq *request = [[SendMessageToWXReq alloc] init];
    request.bText = YES;
    request.text = text;
    request.scene = WXSceneSession;
    [WXApi sendReq:request completion:^(BOOL accepted) {
        dispatch_async(dispatch_get_main_queue(), ^{
            BOOL current = [self.pendingRequestID isEqualToString:requestID];
            [self.correlation completeSendForRequest:requestID accepted:accepted];
            if (current && (!accepted || self.correlation.correlationLost)) { [self.shareTimer invalidate]; self.shareTimer = nil; }
            completion(accepted);
        });
    }];
#else
    completion(NO);
#endif
}
- (BOOL)handleURL:(NSURL *)url {
    if (!self.configured || ![url.scheme isEqualToString:self.appID]) return NO;
#if XY_HAS_SDK
    return [WXApi handleOpenURL:url delegate:self];
#else
    return NO;
#endif
}
- (BOOL)handleUniversalLink:(NSUserActivity *)activity {
    NSURL *url = activity.webpageURL;
    if (!self.configured || !url || !self.universalLink) return NO;
    NSString *prefix = [NSURLComponents componentsWithURL:self.universalLink resolvingAgainstBaseURL:NO].percentEncodedPath;
    NSString *incomingPath = [NSURLComponents componentsWithURL:url resolvingAgainstBaseURL:NO].percentEncodedPath;
    if (![prefix hasSuffix:@"/"]) prefix = [prefix stringByAppendingString:@"/"];
    if (![activity.activityType isEqualToString:NSUserActivityTypeBrowsingWeb] ||
        ![url.scheme isEqualToString:@"https"] || ![url.host.lowercaseString isEqualToString:self.universalLink.host.lowercaseString] ||
        url.user != nil || url.password != nil || (url.port != nil && url.port.integerValue != 443) ||
        ![incomingPath hasPrefix:prefix]) return NO;
#if XY_HAS_SDK
    return [WXApi handleOpenUniversalLink:activity delegate:self];
#else
    return NO;
#endif
}
#if XY_HAS_SDK
- (void)onResp:(BaseResp *)response {
    // iOS 2.0.9 has no transaction field; only one outstanding share can be correlated.
    if (![response isKindOfClass:[SendMessageToWXResp class]]) return;
    int errorCode = response.errCode;
    dispatch_async(dispatch_get_main_queue(), ^{
        NSString *requestID = [self.correlation finishResponse];
        if (!requestID) return;
        [self.shareTimer invalidate];
        self.shareTimer = nil;
        NSString *status = errorCode == WXSuccess ? @"completed" : errorCode == WXErrCodeUserCancel ? @"cancelled" : @"error";
        NSString *code = errorCode == WXSuccess ? @"sdk_completed" : errorCode == WXErrCodeUserCancel ? @"user_cancelled" : @"wechat_sdk_error";
        if (self.resultHandler) self.resultHandler(requestID, status, code);
    });
}
#endif
@end
