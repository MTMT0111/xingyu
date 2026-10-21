package com.xingyu.reply;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.TextView;
import android.widget.Toast;
import java.util.List;

/** Opens the compact assistant on demand; tapping never captures the chat screen. */
public final class WechatReplyBubbleService extends AccessibilityService {
    private static final String TAG = "WechatReplyBubble";
    private static final String WECHAT_PACKAGE = "com.tencent.mm";
    private static final int MAX_CHARS = 6000;
    private WindowManager windows;
    private View bubble;
    private WindowManager.LayoutParams bubbleLayout;
    private Handler handler;
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
        button.setContentDescription("唤起知意，搜索视频或回复消息");
        button.setBackground(circle());
        bubble = button;
        bubbleLayout = overlay(48, 48);
        bubbleLayout.gravity = Gravity.END | Gravity.CENTER_VERTICAL;
        bubbleLayout.x = dp(8);
        button.setAlpha(0.8f);
        button.setOnClickListener(view -> {
            hideBubble();
            try {
                startActivity(new Intent(this, MainActivity.class).setAction(MainActivity.ACTION_FLOATING)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP));
            } catch (RuntimeException error) { showReadFailure("暂时无法打开知意，请从应用图标进入。"); }
        });
        button.setOnTouchListener(new BubbleTouchListener());
        try { windows.addView(bubble, bubbleLayout); }
        catch (RuntimeException error) {
            Log.e(TAG, "Unable to show the WeChat accessibility overlay", error);
            bubble = null;
            bubbleLayout = null;
        }
    }

    private void showReadFailure(String message) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show();
        if (handler != null) {
            handler.removeCallbacks(syncBubbleAction);
            handler.postDelayed(syncBubbleAction, 300);
        }
    }

    private static String packageName(AccessibilityNodeInfo node) {
        CharSequence value = node.getPackageName();
        return value == null ? null : value.toString();
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
