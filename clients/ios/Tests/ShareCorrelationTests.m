#import <XCTest/XCTest.h>
#import "../Sources/XYShareCorrelation.h"

@interface ShareCorrelationTests : XCTestCase
@end
@implementation ShareCorrelationTests
- (void)testNormalResponseBelongsToTheOnlyPendingRequest {
    XYShareCorrelation *state = [[XYShareCorrelation alloc] init];
    XCTAssertTrue([state beginRequest:@"first-request"]);
    XCTAssertFalse([state beginRequest:@"second-request"]);
    [state completeSendForRequest:@"first-request" accepted:YES];
    XCTAssertEqualObjects([state finishResponse], @"first-request");
    XCTAssertNil([state finishResponse]);
    XCTAssertFalse(state.correlationLost);
}
- (void)testTimeoutUnlocksSharingAndNeverAttributesLateResponses {
    XYShareCorrelation *state = [[XYShareCorrelation alloc] init];
    XCTAssertTrue([state beginRequest:@"timed-out-request"]);
    XCTAssertTrue([state expireRequest:@"timed-out-request"]);
    XCTAssertTrue(state.correlationLost);
    XCTAssertNil(state.pendingRequestID);
    XCTAssertTrue([state beginRequest:@"next-request"]);
    XCTAssertNil([state finishResponse]);
    XCTAssertEqualObjects(state.pendingRequestID, @"next-request");
    [state completeSendForRequest:@"next-request" accepted:YES];
    XCTAssertNil(state.pendingRequestID);
    XCTAssertNil([state finishResponse]);
    XCTAssertTrue([state beginRequest:@"third-request"]);
}
- (void)testStaleTimerAndCompletionDoNotClearNewRequest {
    XYShareCorrelation *state = [[XYShareCorrelation alloc] init];
    XCTAssertTrue([state beginRequest:@"expired-request"]);
    XCTAssertTrue([state expireRequest:@"expired-request"]);
    XCTAssertTrue([state beginRequest:@"current-request"]);
    XCTAssertFalse([state expireRequest:@"expired-request"]);
    [state completeSendForRequest:@"expired-request" accepted:YES];
    XCTAssertEqualObjects(state.pendingRequestID, @"current-request");
}
- (void)testRejectedSendReleasesPendingWithoutLosingCorrelation {
    XYShareCorrelation *state = [[XYShareCorrelation alloc] init];
    XCTAssertTrue([state beginRequest:@"rejected-request"]);
    [state completeSendForRequest:@"rejected-request" accepted:NO];
    XCTAssertNil(state.pendingRequestID);
    XCTAssertFalse(state.correlationLost);
    XCTAssertTrue([state beginRequest:@"retry-request"]);
}
@end
