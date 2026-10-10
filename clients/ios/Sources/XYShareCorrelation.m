#import "XYShareCorrelation.h"
@interface XYShareCorrelation ()
@property(nonatomic, readwrite, copy, nullable) NSString *pendingRequestID;
@property(nonatomic, readwrite) BOOL correlationLost;
@end
@implementation XYShareCorrelation
- (BOOL)beginRequest:(NSString *)requestID {
    if (self.pendingRequestID || requestID.length == 0) return NO;
    self.pendingRequestID = requestID;
    return YES;
}
- (void)completeSendForRequest:(NSString *)requestID accepted:(BOOL)accepted {
    if ([self.pendingRequestID isEqualToString:requestID] && (!accepted || self.correlationLost)) self.pendingRequestID = nil;
}
- (BOOL)expireRequest:(NSString *)requestID {
    if (![self.pendingRequestID isEqualToString:requestID]) return NO;
    self.correlationLost = YES;
    self.pendingRequestID = nil;
    return YES;
}
- (nullable NSString *)finishResponse {
    if (self.correlationLost) return nil;
    NSString *requestID = self.pendingRequestID;
    self.pendingRequestID = nil;
    return requestID;
}
@end
