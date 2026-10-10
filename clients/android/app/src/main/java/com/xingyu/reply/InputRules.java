package com.xingyu.reply;

import java.net.URI;
import java.net.URISyntaxException;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

/** Input rules are independent of Android so boundary checks run on the JVM. */
public final class InputRules {
    public static final String USB_SERVICE_URL = "http://127.0.0.1:4173/";
    public static final int MAX_INBOUND_TEXT_CODE_POINTS = 6000;
    public static final long MAX_IMAGE_BYTES = 4L * 1024 * 1024;
    public static final long MAX_VIDEO_BYTES = 20L * 1024 * 1024;
    private InputRules() {}

    public static String serviceUrl(String input) {
        return serviceUrl(input, false);
    }

    public static String serviceUrl(String input, boolean allowUsbDebug) {
        if (input == null || input.length() > 2048) throw new IllegalArgumentException("INVALID_SERVICE_URL");
        URI uri = parse(input.trim());
        if (!validOrigin(uri, allowUsbDebug) || uri.getRawQuery() != null || uri.getRawFragment() != null ||
            !(uri.getRawPath().isEmpty() || uri.getRawPath().equals("/"))) {
            throw new IllegalArgumentException("INVALID_SERVICE_URL");
        }
        return origin(uri) + "/";
    }

    public static String origin(String serviceUrl) {
        return origin(serviceUrl, false);
    }

    public static String origin(String serviceUrl, boolean allowUsbDebug) {
        return origin(parse(serviceUrl(serviceUrl, allowUsbDebug)));
    }

    public static boolean sameOrigin(String configuredUrl, String candidate) {
        return sameOrigin(configuredUrl, candidate, false);
    }

    public static boolean sameOrigin(String configuredUrl, String candidate, boolean allowUsbDebug) {
        try {
            URI uri = parse(candidate);
            return validOrigin(uri, allowUsbDebug) && origin(configuredUrl, allowUsbDebug).equals(origin(uri));
        } catch (IllegalArgumentException error) { return false; }
    }

    public static boolean validAppId(String value) {
        return value != null && (value.isEmpty() || value.matches("wx[0-9a-fA-F]{16}"));
    }

    public static boolean validRequestId(String value) {
        return value != null && value.matches("[A-Za-z0-9._:-]{1,128}");
    }

    public static boolean validText(String text) {
        if (text == null || text.isEmpty() || text.getBytes(StandardCharsets.UTF_8).length > 10240) return false;
        return validUnicodeContent(text);
    }

    public static String inboundText(CharSequence value) {
        if (value == null) throw new IllegalArgumentException("MISSING_TEXT");
        String text = value.toString();
        if (text.codePointCount(0, text.length()) > MAX_INBOUND_TEXT_CODE_POINTS || !validUnicodeContent(text)) {
            throw new IllegalArgumentException("INVALID_TEXT");
        }
        return text;
    }

    public static String mediaMime(String declared, String resolved) {
        String declaredMime = mime(declared), resolvedMime = mime(resolved);
        if (supportedMediaMime(resolvedMime) && (declaredMime.equals(resolvedMime) ||
            declaredMime.equals(resolvedMime.substring(0, resolvedMime.indexOf('/')) + "/*"))) return resolvedMime;
        if (resolvedMime.isEmpty() && supportedMediaMime(declaredMime)) return declaredMime;
        throw new IllegalArgumentException("UNSUPPORTED_MEDIA");
    }

    public static boolean plainTextMime(String value) {
        return mime(value).equals("text/plain");
    }

    public static String mediaKind(String mime) {
        if (!supportedMediaMime(mime)) throw new IllegalArgumentException("UNSUPPORTED_MEDIA");
        return mime.startsWith("image/") ? "image" : "video";
    }

    public static long maxMediaBytes(String mime) {
        return mediaKind(mime).equals("image") ? MAX_IMAGE_BYTES : MAX_VIDEO_BYTES;
    }

    public static String safeDisplayName(String value, String mime) {
        String fallback = mediaKind(mime).equals("image") ? "分享的图片" : "分享的视频";
        if (value == null) return fallback;
        StringBuilder clean = new StringBuilder();
        value.codePoints().limit(120).forEach(codePoint -> {
            if (codePoint >= 32 && codePoint != 127 && codePoint != '/' && codePoint != '\\') clean.appendCodePoint(codePoint);
        });
        String name = clean.toString().trim().replaceFirst("^\\.+", "");
        return name.isEmpty() ? fallback : name;
    }

    private static boolean validUnicodeContent(String text) {
        if (text.isEmpty()) return false;
        boolean hasContent = false;
        for (int index = 0; index < text.length(); index++) {
            char value = text.charAt(index);
            if (!Character.isWhitespace(value) && !Character.isSpaceChar(value)) hasContent = true;
            if (value == 0) return false;
            if (Character.isHighSurrogate(value)) {
                if (++index >= text.length() || !Character.isLowSurrogate(text.charAt(index))) return false;
            } else if (Character.isLowSurrogate(value)) return false;
        }
        return hasContent;
    }

    private static String mime(String value) {
        if (value == null) return "";
        String normalized = value.trim().toLowerCase(Locale.ROOT);
        int parameters = normalized.indexOf(';');
        normalized = parameters < 0 ? normalized : normalized.substring(0, parameters).trim();
        return normalized.equals("image/jpg") ? "image/jpeg" : normalized;
    }

    private static boolean supportedMediaMime(String mime) {
        return mime.equals("image/jpeg") || mime.equals("image/png") || mime.equals("image/webp") ||
            mime.equals("video/mp4") || mime.equals("video/webm") || mime.equals("video/quicktime");
    }

    private static URI parse(String value) {
        try { return new URI(value); }
        catch (URISyntaxException | NullPointerException error) { throw new IllegalArgumentException("INVALID_SERVICE_URL"); }
    }

    private static boolean validOrigin(URI uri, boolean allowUsbDebug) {
        String host = uri.getHost();
        boolean usbDebug = allowUsbDebug && "http".equalsIgnoreCase(uri.getScheme())
            && "127.0.0.1".equals(host) && uri.getPort() == 4173;
        if ((!"https".equalsIgnoreCase(uri.getScheme()) && !usbDebug) || uri.getRawUserInfo() != null ||
            host == null || host.length() > 253 || uri.getPort() == 0 || uri.getPort() > 65535 || uri.getPort() < -1) return false;
        if (uri.getRawAuthority().contains("@") || uri.getRawAuthority().endsWith(":")) return false;
        for (String label : host.split("\\.", -1)) {
            if (!label.matches("[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?")) return false;
        }
        return true;
    }

    private static String origin(URI uri) {
        String scheme = uri.getScheme().toLowerCase(Locale.ROOT);
        return scheme + "://" + uri.getHost().toLowerCase(Locale.ROOT)
            + (uri.getPort() == -1 || (scheme.equals("https") && uri.getPort() == 443) ? "" : ":" + uri.getPort());
    }
}
