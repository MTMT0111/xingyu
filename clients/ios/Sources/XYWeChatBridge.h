#import <UIKit/UIKit.h>
NS_ASSUME_NONNULL_BEGIN
@interface XYWeChatBridge : NSObject
@property(nonatomic, readonly) BOOL sdkAvailable;
@property(nonatomic, readonly) BOOL configured;
@property(nonatomic, readonly, nullable) NSString *pendingRequestID;
@property(nonatomic, copy, nullable) void (^resultHandler)(NSString *requestID, NSString *status, NSString *code);
- (BOOL)configureAppID:(NSString *)appID universalLink:(NSString *)universalLink NS_SWIFT_NAME(configure(appID:universalLink:));
- (BOOL)isWeChatInstalled NS_SWIFT_NAME(isWeChatInstalled());
- (void)shareText:(NSString *)text requestID:(NSString *)requestID completion:(void (^)(BOOL accepted))completion NS_SWIFT_NAME(share(text:requestID:completion:));
- (BOOL)handleURL:(NSURL *)url NS_SWIFT_NAME(handle(url:));
- (BOOL)handleUniversalLink:(NSUserActivity *)activity NS_SWIFT_NAME(handle(activity:));
@end
NS_ASSUME_NONNULL_END
