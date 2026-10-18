package com.xingyu.reply;

import java.util.UUID;
import java.util.Timer;
import java.util.TimerTask;

/** Holds one user-requested screenshot in memory until MainActivity consumes it. */
final class ScreenCaptureStore {
    private static final long TTL_MILLIS = 120_000L;
    private static final long TTL_NANOS = 120_000_000_000L;
    private static final Timer CLEANUP = new Timer("screen-capture-cleanup", true);
    private static byte[] image;
    private static String token;
    private static long expiresAt;

    private ScreenCaptureStore() {}

    static synchronized String put(byte[] value) {
        if (value == null || value.length == 0 || value.length > InputRules.MAX_IMAGE_BYTES) {
            throw new IllegalArgumentException("INVALID_SCREEN_CAPTURE");
        }
        image = value.clone();
        token = UUID.randomUUID().toString();
        expiresAt = System.nanoTime() + TTL_NANOS;
        String issued = token;
        CLEANUP.schedule(new TimerTask() {
            @Override public void run() { discard(issued); }
        }, TTL_MILLIS);
        return issued;
    }

    static synchronized byte[] take(String provided) {
        if (token == null || System.nanoTime() >= expiresAt) {
            clear();
            return null;
        }
        if (provided == null || !token.equals(provided)) return null;
        byte[] value = image;
        clear();
        return value;
    }

    static synchronized void discard(String provided) {
        if (provided != null && provided.equals(token)) clear();
    }

    private static void clear() {
        image = null;
        token = null;
        expiresAt = 0;
    }
}
