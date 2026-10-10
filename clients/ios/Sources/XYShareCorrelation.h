#import <Foundation/Foundation.h>
NS_ASSUME_NONNULL_BEGIN
@interface XYShareCorrelation : NSObject
@property(nonatomic, readonly, nullable) NSString *pendingRequestID;
@property(nonatomic, readonly) BOOL correlationLost;
- (BOOL)beginRequest:(NSString *)requestID NS_SWIFT_NAME(begin(requestID:));
- (void)completeSendForRequest:(NSString *)requestID accepted:(BOOL)accepted NS_SWIFT_NAME(completeSend(requestID:accepted:));
- (BOOL)expireRequest:(NSString *)requestID NS_SWIFT_NAME(expire(requestID:));
- (nullable NSString *)finishResponse NS_SWIFT_NAME(finishResponse());
@end
NS_ASSUME_NONNULL_END
