package com.xingyu.reply;

import org.junit.Test;
import static org.junit.Assert.*;

public final class InputRulesTest {
    @Test public void canonicalizesOnlyHttpsRoot() {
        assertEquals("https://example.com/", InputRules.serviceUrl(" HTTPS://EXAMPLE.COM:443 "));
        assertEquals("https://example.com:8443/", InputRules.serviceUrl("https://example.com:8443/"));
        assertEquals("https://127.0.0.1/", InputRules.serviceUrl("https://127.0.0.1/"));
    }

    @Test public void rejectsUnsafeOrAmbiguousServiceUrls() {
        String[] invalid = {"", "http://example.com", "javascript:alert(1)", "file:///tmp/a", "//example.com",
            "https://user:pass@example.com/", "https://example.com@evil.test/", "https://example.com:0/",
            "https://example.com:65536/", "https://example.com:/", "https://example.com:abc/",
            "https://example.com/path", "https://example.com//", "https://example.com/%2f",
            "https://example.com/?key=value", "https://example.com/#fragment", "https://example.com./",
            "https://-bad.example/", "https://bad..example/", "https://example.com\\@evil.test/",
            "https://exa mple.com", "https://[::1]/"};
        for (String value : invalid) assertThrows(value, IllegalArgumentException.class, () -> InputRules.serviceUrl(value));
        assertThrows(IllegalArgumentException.class, () -> InputRules.serviceUrl(null));
    }

    @Test public void originIsExactButAllowsPagePaths() {
        String root = "https://example.com:443/";
        assertTrue(InputRules.sameOrigin(root, "https://EXAMPLE.COM/app?view=reply#draft"));
        assertFalse(InputRules.sameOrigin(root, "https://example.com.evil.test/"));
        assertFalse(InputRules.sameOrigin(root, "https://sub.example.com/"));
        assertFalse(InputRules.sameOrigin(root, "https://example.com:8443/"));
        assertFalse(InputRules.sameOrigin(root, "http://example.com/"));
        assertFalse(InputRules.sameOrigin(root, "https://user@example.com/"));
        assertFalse(InputRules.sameOrigin(root, "data:text/html,hello"));
        assertFalse(InputRules.sameOrigin(root, null));
    }

    @Test public void appIdAndRequestIdsHaveStrictBoundaries() {
        assertTrue(InputRules.validAppId(""));
        assertTrue(InputRules.validAppId("wx0123456789abcdef"));
        assertFalse(InputRules.validAppId("wx0123456789abcdefg"));
        assertFalse(InputRules.validAppId(" wx0123456789abcdef"));
        assertFalse(InputRules.validAppId(null));
        assertTrue(InputRules.validRequestId("reply:1710000-abc_1.2"));
        assertFalse(InputRules.validRequestId(""));
        assertFalse(InputRules.validRequestId("x\n"));
        assertFalse(InputRules.validRequestId("a".repeat(129)));
    }

    @Test public void sharesExactTextWithinUtf8Limit() {
        assertTrue(InputRules.validText("妈，放心，我晚点给你打电话。"));
        assertTrue(InputRules.validText("a".repeat(10240)));
        assertFalse(InputRules.validText("a".repeat(10241)));
        assertTrue(InputRules.validText("妈".repeat(3413)));
        assertFalse(InputRules.validText("妈".repeat(3414)));
        assertTrue(InputRules.validText("🌹".repeat(2560)));
        assertFalse(InputRules.validText("🌹".repeat(2561)));
        for (String value : new String[] {"", " \t\n", "\u3000\u00a0", "a\u0000b", "\uD800", "\uDC00"}) {
            assertFalse(InputRules.validText(value));
        }
        assertFalse(InputRules.validText(null));
    }
}
