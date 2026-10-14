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

    @Test public void usbDebugAllowsOnlyFixedLoopbackService() {
        assertEquals(InputRules.USB_SERVICE_URL, InputRules.serviceUrl(" HTTP://127.0.0.1:4173 ", true));
        assertEquals("http://127.0.0.1:4173", InputRules.origin(InputRules.USB_SERVICE_URL, true));
        String[] invalid = {"http://localhost:4173/", "http://127.0.0.2:4173/", "http://10.0.2.2:4173/",
            "http://192.168.1.10:4173/", "http://127.0.0.1/", "http://127.0.0.1:4174/",
            "http://127.0.0.1.evil.test:4173/", "http://user@127.0.0.1:4173/", "http://[::1]:4173/",
            "http://127.0.0.1:4173/path", "http://127.0.0.1:4173/?key=value", "http://127.0.0.1:4173/#fragment"};
        for (String value : invalid) assertThrows(value, IllegalArgumentException.class, () -> InputRules.serviceUrl(value, true));
    }

    @Test public void usbDebugStillEnforcesExactPageOrigin() {
        String root = InputRules.USB_SERVICE_URL;
        assertTrue(InputRules.sameOrigin(root, "http://127.0.0.1:4173/app?view=reply#draft", true));
        for (String value : new String[] {"http://127.0.0.1:4174/app", "https://127.0.0.1:4173/",
            "http://localhost:4173/", "http://127.0.0.1.evil.test:4173/", "http://user@127.0.0.1:4173/",
            "file:///tmp/a", "javascript:alert(1)", null}) {
            assertFalse(InputRules.sameOrigin(root, value, true));
        }
        assertFalse(InputRules.sameOrigin("https://example.com/", root, true));
    }

    @Test public void releaseRejectsUsbEvenIfPreviouslySaved() {
        assertThrows(IllegalArgumentException.class, () -> InputRules.serviceUrl(InputRules.USB_SERVICE_URL));
        assertThrows(IllegalArgumentException.class, () -> InputRules.serviceUrl(InputRules.USB_SERVICE_URL, false));
        assertThrows(IllegalArgumentException.class, () -> InputRules.origin(InputRules.USB_SERVICE_URL, false));
        assertFalse(InputRules.sameOrigin(InputRules.USB_SERVICE_URL, InputRules.USB_SERVICE_URL, false));
        assertEquals("https://example.com/", InputRules.serviceUrl("https://example.com", false));
    }

    @Test public void buildVariantControlsUsbOptIn() {
        if (BuildConfig.DEBUG) {
            assertEquals(InputRules.USB_SERVICE_URL, InputRules.serviceUrl(InputRules.USB_SERVICE_URL, BuildConfig.DEBUG));
        } else {
            assertThrows(IllegalArgumentException.class, () -> InputRules.serviceUrl(InputRules.USB_SERVICE_URL, BuildConfig.DEBUG));
        }
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

    @Test public void quickReplyRequiresTheExactOneTimeToken() {
        assertTrue(InputRules.quickReplyTokenMatches("token-1", "token-1"));
        assertFalse(InputRules.quickReplyTokenMatches("token-2", "token-1"));
        assertFalse(InputRules.quickReplyTokenMatches(null, "token-1"));
        assertFalse(InputRules.quickReplyTokenMatches("token-1", null));
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

    @Test public void parsesInboundTextAtThePageBoundary() {
        assertTrue(InputRules.plainTextMime(" Text/Plain; charset=UTF-8 "));
        assertFalse(InputRules.plainTextMime("text/html"));
        assertFalse(InputRules.plainTextMime(null));
        assertEquals(" 妈，晚点回你。 ", InputRules.inboundText(" 妈，晚点回你。 "));
        assertEquals("🌹".repeat(6000), InputRules.inboundText("🌹".repeat(6000)));
        for (CharSequence value : new CharSequence[] {null, "", " \t\n", "a\u0000b", "\uD800", "🌹".repeat(6001)}) {
            assertThrows(IllegalArgumentException.class, () -> InputRules.inboundText(value));
        }
    }

    @Test public void resolvesOnlySupportedMediaTypesAndLimits() {
        assertEquals("image/jpeg", InputRules.mediaMime("image/*", "IMAGE/JPEG; charset=binary"));
        assertEquals("image/jpeg", InputRules.mediaMime("image/jpg", null));
        assertEquals("video/mp4", InputRules.mediaMime(" video/mp4 ", null));
        assertEquals("image", InputRules.mediaKind("image/webp"));
        assertEquals("video", InputRules.mediaKind("video/quicktime"));
        assertEquals(4L * 1024 * 1024, InputRules.maxMediaBytes("image/png"));
        assertEquals(20L * 1024 * 1024, InputRules.maxMediaBytes("video/webm"));
        for (String[] types : new String[][] {{"image/*", null}, {"*/*", "image/png"}, {"image/*", "video/mp4"},
            {"image/gif", "image/gif"}, {"text/plain", null}, {null, null}}) {
            assertThrows(IllegalArgumentException.class, () -> InputRules.mediaMime(types[0], types[1]));
        }
    }

    @Test public void cleansProviderFileNamesWithoutTrustingPaths() {
        assertEquals("etcpasswd.jpg", InputRules.safeDisplayName("../../etc\\passwd.jpg", "image/jpeg"));
        assertEquals("分享的视频", InputRules.safeDisplayName("\u0000/\\\t", "video/mp4"));
        String longName = InputRules.safeDisplayName("图".repeat(130), "image/png");
        assertEquals(120, longName.codePointCount(0, longName.length()));
    }
}
