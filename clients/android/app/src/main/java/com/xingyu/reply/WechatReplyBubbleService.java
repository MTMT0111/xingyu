package com.xingyu.reply;

import android.accessibilityservice.AccessibilityService;
import android.annotation.TargetApi;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.hardware.HardwareBuffer;
import android.content.Intent;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Gravity;
import android.view.Display;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.TextView;
import android.widget.Toast;
import java.io.ByteArrayOutputStream;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Captures the current WeChat screen only after the user presses the floating action. */
public final class WechatReplyBubbleService extends AccessibilityService {
    private static final String TAG = "WechatReplyBubble";
    private static final String WECHAT_PACKAGE = "com.tencent.mm";
    private static final int MAX_NODES = 1200;
    private static final int MAX_CHARS = 6000;
    private WindowManager windows;
    private View bubble;
    private WindowManager.LayoutParams bubbleLayout;
    private Handler handler;
    private final ExecutorService captureExecutor = Executors.newSingleThreadExecutor();
    private volatile boolean destroyed;
    private final Runnable syncBubbleAction = this::syncBubble;

    @Override public void onServiceConnected() {
        super.onServiceConnected();
        if (!getSharedPreferences(MainActivity.PREFS_NAME, MODE_PRIVATE)
            .getBoolean(MainActivity.PREF_WECHAT_BUBBLE_CONSENT, false)) {
            Toast.makeText(this, "请先在知意阅读并同意悬浮球的数据说明。", Toast.LENGTH_LONG).show();
            disableSelf();
            return;
        }
        windows = getSystemService(WindowManager.class);
        handler = new Handler(Looper.getMainLooper());
        handler.post(syncBubbleAction);
    }

    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null || event.getEventType() != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return;
        if (handler == null) return;
        handler.removeCallbacks(syncBubbleAction);
        handler.postDelayed(syncBubbleAction, 100);
    }

    @Override public void onInterrupt() { hideBubble(); }

    static boolean isWechatPackage(String packageName) { return WECHAT_PACKAGE.equals(packageName); }

    private void syncBubble() {
        if (isWechatForeground()) showBubble();
        else hideBubble();
    }

    private boolean isWechatForeground() {
        AccessibilityNodeInfo root = null;
        try {
            root = getRootInActiveWindow();
            if (root != null && isWechatPackage(packageName(root))) return true;
        } catch (RuntimeException error) {
            Log.w(TAG, "Unable to read the active accessibility root", error);
        } finally {
            if (root != null) root.recycle();
        }

        boolean found = false;
        try {
            List<AccessibilityWindowInfo> activeWindows = getWindows();
            for (AccessibilityWindowInfo window : activeWindows) {
                AccessibilityNodeInfo candidate = null;
                try {
                    if (window.getType() == AccessibilityWindowInfo.TYPE_APPLICATION && window.isActive()) {
                        candidate = window.getRoot();
                        found |= candidate != null && isWechatPackage(packageName(candidate));
                    }
                } finally {
                    if (candidate != null) candidate.recycle();
                    window.recycle();
                }
            }
        } catch (RuntimeException error) {
            Log.w(TAG, "Unable to inspect accessibility windows", error);
        }
        return found;
    }

    private void showBubble() {
        if (windows == null || bubble != null) return;
        TextView button = new TextView(this) {
            @Override public boolean performClick() { return super.performClick(); }
        };
        button.setText("言");
        button.setTextSize(16);
        button.setTextColor(Color.rgb(31, 50, 69));
        button.setGravity(Gravity.CENTER);
        button.setContentDescription("知意，读取微信当前页");
        button.setBackground(circle());
        bubble = button;
        bubbleLayout = overlay(56, 56);
        bubbleLayout.gravity = Gravity.END | Gravity.CENTER_VERTICAL;
        bubbleLayout.x = dp(8);
        button.setOnClickListener(view -> readCurrentPage());
        button.setOnTouchListener(new BubbleTouchListener());
        try { windows.addView(bubble, bubbleLayout); }
        catch (RuntimeException error) {
            Log.e(TAG, "Unable to show the WeChat accessibility overlay", error);
            bubble = null;
            bubbleLayout = null;
        }
    }

    private void readCurrentPage() {
        hideBubble();
        AccessibilityNodeInfo root = findWechatRoot();
        StringBuilder text = new StringBuilder();
        int[] visited = {0};
        try { if (root != null) collectVisibleText(root, text, visited); }
        catch (RuntimeException error) {
            showReadFailure("微信页面刚刚变化，未能读取；请再点一次。");
            return;
        } finally { if (root != null) root.recycle(); }
        String visibleText = text.length() == 0 ? null : text.toString();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            captureCurrentScreen(visibleText);
            return;
        }
        if (visibleText == null) {
            showReadFailure("微信没有提供可见文字。小视频请长按保存或分享给知意分析。");
            return;
        }
        launchText(visibleText);
    }

    private void launchText(String message) {
        try {
            Intent intent = new Intent(this, MainActivity.class)
                .setAction(Intent.ACTION_SEND)
                .setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, InputRules.inboundText(message))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(intent);
        } catch (IllegalArgumentException error) {
            showReadFailure("当前页面没有可分析的文字。小视频请长按保存或分享给知意分析。");
        } catch (RuntimeException error) {
            showReadFailure("无法打开分析页面；微信内容没有发送。");
        }
    }

    @TargetApi(Build.VERSION_CODES.R)
    private void captureCurrentScreen(String visibleText) {
        handler.postDelayed(() -> {
            if (destroyed) return;
            if (!isWechatForeground()) {
                showReadFailure("微信已离开前台，本次没有截取屏幕。");
                return;
            }
            takeScreenshot(Display.DEFAULT_DISPLAY, captureExecutor, new TakeScreenshotCallback() {
            @Override public void onSuccess(ScreenshotResult result) {
                byte[] image = encodeScreenshot(result);
                handler.post(() -> {
                    if (destroyed) return;
                    if (image == null) {
                        if (visibleText != null) launchText(visibleText);
                        else showReadFailure("当前画面无法截取。小视频请长按保存或分享给知意分析。");
                        return;
                    }
                    String token = null;
                    try {
                        token = ScreenCaptureStore.put(image);
                        Intent intent = new Intent(WechatReplyBubbleService.this, MainActivity.class)
                            .setAction(MainActivity.ACTION_SCREEN_CAPTURE)
                            .putExtra(MainActivity.EXTRA_SCREEN_CAPTURE_TOKEN, token)
                            .putExtra(MainActivity.EXTRA_SCREEN_VISIBLE_TEXT, visibleText)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
                        startActivity(intent);
                    } catch (RuntimeException error) {
                        ScreenCaptureStore.discard(token);
                        showReadFailure("无法打开屏幕分析；当前画面没有发送。");
                    }
                });
            }
            @Override public void onFailure(int errorCode) {
                handler.post(() -> {
                    if (destroyed) return;
                    if (visibleText != null) launchText(visibleText);
                    else showReadFailure("当前画面无法截取。小视频请长按保存或分享给知意分析。");
                });
            }
            });
        }, 180);
    }

    @TargetApi(Build.VERSION_CODES.R)
    private static byte[] encodeScreenshot(ScreenshotResult result) {
        HardwareBuffer buffer = result.getHardwareBuffer();
        try {
            Bitmap wrapped = Bitmap.wrapHardwareBuffer(buffer, result.getColorSpace());
            Bitmap source = wrapped == null ? null : wrapped.copy(Bitmap.Config.ARGB_8888, false);
            if (source == null) return null;
            Bitmap image = source;
            int longest = Math.max(source.getWidth(), source.getHeight());
            if (longest > 1920) {
                float scale = 1920f / longest;
                image = Bitmap.createScaledBitmap(source, Math.max(1, Math.round(source.getWidth() * scale)),
                    Math.max(1, Math.round(source.getHeight() * scale)), true);
                source.recycle();
            }
            try {
                ByteArrayOutputStream output = new ByteArrayOutputStream();
                for (int quality : new int[] {86, 70, 55}) {
                    output.reset();
                    if (!image.compress(Bitmap.CompressFormat.JPEG, quality, output)) return null;
                    if (output.size() <= InputRules.MAX_IMAGE_BYTES) return output.toByteArray();
                }
                return null;
            } finally { image.recycle(); }
        } catch (RuntimeException error) {
            return null;
        } finally { buffer.close(); }
    }

    private void showReadFailure(String message) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show();
        if (handler != null) {
            handler.removeCallbacks(syncBubbleAction);
            handler.postDelayed(syncBubbleAction, 300);
        }
    }

    private AccessibilityNodeInfo findWechatRoot() {
        try {
            List<AccessibilityWindowInfo> activeWindows = getWindows();
            for (AccessibilityWindowInfo window : activeWindows) {
                AccessibilityNodeInfo root = null;
                try {
                    if (window.getType() != AccessibilityWindowInfo.TYPE_APPLICATION) continue;
                    root = window.getRoot();
                    if (root != null && isWechatPackage(packageName(root))) return root;
                    if (root != null) root.recycle();
                } finally { window.recycle(); }
            }
        } catch (RuntimeException ignored) { }
        try {
            AccessibilityNodeInfo root = getRootInActiveWindow();
            if (root != null && isWechatPackage(packageName(root))) return root;
            if (root != null) root.recycle();
        } catch (RuntimeException ignored) { }
        return null;
    }

    private static String packageName(AccessibilityNodeInfo node) {
        CharSequence value = node.getPackageName();
        return value == null ? null : value.toString();
    }

    private static void collectVisibleText(AccessibilityNodeInfo node, StringBuilder output, int[] visited) {
        if (node == null || visited[0]++ >= MAX_NODES || output.codePointCount(0, output.length()) >= MAX_CHARS ||
            !node.isVisibleToUser() || node.isPassword()) return;
        CharSequence text = node.getText();
        CharSequence description = node.getContentDescription();
        appendVisibleText(text, output);
        if (description != null && !description.equals(text)) appendVisibleText(description, output);
        for (int i = 0; i < node.getChildCount() && visited[0] < MAX_NODES && output.codePointCount(0, output.length()) < MAX_CHARS; i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child != null) {
                try { collectVisibleText(child, output, visited); }
                finally { child.recycle(); }
            }
        }
    }

    static void appendVisibleText(CharSequence value, StringBuilder output) {
        if (value == null || output.codePointCount(0, output.length()) >= MAX_CHARS) return;
        String line = value.toString().trim();
        if (line.isEmpty()) return;
        int current = output.codePointCount(0, output.length());
        int available = MAX_CHARS - current - (current > 0 ? 1 : 0);
        if (available <= 0) return;
        int count = line.codePointCount(0, line.length());
        if (count > available) line = line.substring(0, line.offsetByCodePoints(0, available));
        if (output.length() > 0) output.append('\n');
        output.append(line);
    }

    private void hideBubble() {
        if (windows != null && bubble != null) {
            try { windows.removeView(bubble); } catch (RuntimeException ignored) { }
        }
        bubble = null;
        bubbleLayout = null;
    }

    private WindowManager.LayoutParams overlay(int width, int height) {
        return new WindowManager.LayoutParams(dp(width), height < 0 ? height : dp(height),
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
            PixelFormat.TRANSLUCENT);
    }

    private GradientDrawable circle() {
        GradientDrawable shape = new GradientDrawable();
        shape.setShape(GradientDrawable.OVAL);
        shape.setColor(Color.rgb(246, 202, 75));
        shape.setStroke(dp(2), Color.WHITE);
        return shape;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    @Override public void onDestroy() {
        destroyed = true;
        captureExecutor.shutdownNow();
        if (handler != null) handler.removeCallbacksAndMessages(null);
        hideBubble();
        super.onDestroy();
    }

    private final class BubbleTouchListener implements View.OnTouchListener {
        private float downX, downY;
        private int startX, startY;
        private boolean moved;
        @Override public boolean onTouch(View view, MotionEvent event) {
            if (bubbleLayout == null || windows == null) return false;
            if (event.getAction() == MotionEvent.ACTION_DOWN) {
                downX = event.getRawX();
                downY = event.getRawY();
                startX = bubbleLayout.x;
                startY = bubbleLayout.y;
                moved = false;
                return true;
            }
            if (event.getAction() == MotionEvent.ACTION_MOVE) {
                int dx = Math.round(downX - event.getRawX());
                int dy = Math.round(event.getRawY() - downY);
                moved |= Math.abs(dx) + Math.abs(dy) > dp(8);
                bubbleLayout.x = Math.max(0, startX + dx);
                bubbleLayout.y = startY + dy;
                try { windows.updateViewLayout(bubble, bubbleLayout); } catch (RuntimeException ignored) { }
                return true;
            }
            if (event.getAction() == MotionEvent.ACTION_UP) {
                if (!moved) view.performClick();
                return true;
            }
            return event.getAction() == MotionEvent.ACTION_CANCEL;
        }
    }
}
