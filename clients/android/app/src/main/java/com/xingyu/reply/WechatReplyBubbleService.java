package com.xingyu.reply;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.content.Intent;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import java.util.List;

/** Reads visible WeChat text only after the user presses the floating action. */
public final class WechatReplyBubbleService extends AccessibilityService {
    private static final String WECHAT_PACKAGE = "com.tencent.mm";
    private static final int MAX_NODES = 1200;
    private static final int MAX_CHARS = 6000;
    private WindowManager windows;
    private View bubble, menu;
    private WindowManager.LayoutParams bubbleLayout, menuLayout;
    private boolean menuOpen;

    @Override public void onServiceConnected() {
        super.onServiceConnected();
        if (!getSharedPreferences(MainActivity.PREFS_NAME, MODE_PRIVATE)
            .getBoolean(MainActivity.PREF_WECHAT_BUBBLE_CONSENT, false)) {
            Toast.makeText(this, "请先在言外阅读并同意悬浮球的数据说明。", Toast.LENGTH_LONG).show();
            disableSelf();
            return;
        }
        windows = getSystemService(WindowManager.class);
    }

    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null || event.getEventType() != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return;
        CharSequence packageName = event.getPackageName();
        if (isWechatPackage(packageName == null ? null : packageName.toString())) showBubble();
        else hideBubble();
    }

    @Override public void onInterrupt() { hideBubble(); }

    static boolean isWechatPackage(String packageName) { return WECHAT_PACKAGE.equals(packageName); }

    private void showBubble() {
        if (windows == null || bubble != null) return;
        TextView button = new TextView(this);
        button.setText("言");
        button.setTextSize(16);
        button.setTextColor(Color.rgb(31, 50, 69));
        button.setGravity(Gravity.CENTER);
        button.setContentDescription("言外，读取微信当前页");
        button.setBackground(circle());
        bubble = button;
        bubbleLayout = overlay(56, 56);
        bubbleLayout.gravity = Gravity.RIGHT | Gravity.CENTER_VERTICAL;
        bubbleLayout.x = dp(8);
        button.setOnTouchListener(new BubbleTouchListener());
        try { windows.addView(bubble, bubbleLayout); }
        catch (RuntimeException error) { bubble = null; bubbleLayout = null; }
    }

    private void toggleMenu() {
        if (menuOpen) { hideMenu(); return; }
        if (windows == null || bubble == null) return;
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(14), dp(12), dp(14), dp(12));
        panel.setBackground(rounded(Color.WHITE, dp(14)));
        panel.setElevation(dp(8));
        TextView title = new TextView(this);
        title.setText("微信当前页面");
        title.setTextSize(15);
        title.setTextColor(Color.rgb(31, 50, 69));
        title.setGravity(Gravity.CENTER_VERTICAL);
        panel.addView(title, new LinearLayout.LayoutParams(-1, dp(38)));
        Button read = panelButton("读取可见文字", Color.rgb(246, 202, 75));
        read.setOnClickListener(view -> readCurrentPage());
        panel.addView(read, new LinearLayout.LayoutParams(-1, dp(44)));
        Button close = panelButton("关闭悬浮球", Color.rgb(242, 244, 246));
        close.setOnClickListener(view -> disableSelf());
        LinearLayout.LayoutParams closeLayout = new LinearLayout.LayoutParams(-1, dp(40));
        closeLayout.topMargin = dp(6);
        panel.addView(close, closeLayout);
        menu = panel;
        menuLayout = overlay(236, -2);
        menuLayout.gravity = Gravity.RIGHT | Gravity.CENTER_VERTICAL;
        menuLayout.x = dp(70);
        try {
            windows.addView(menu, menuLayout);
            menuOpen = true;
        } catch (RuntimeException error) { menu = null; menuLayout = null; }
    }

    private void readCurrentPage() {
        hideMenu();
        AccessibilityNodeInfo root = findWechatRoot();
        if (root == null) {
            Toast.makeText(this, "微信没有提供当前页面的可访问文字。可复制文字或使用微信分享入口。", Toast.LENGTH_LONG).show();
            return;
        }
        StringBuilder text = new StringBuilder();
        int[] visited = {0};
        try { collectVisibleText(root, text, visited); }
        catch (RuntimeException error) {
            Toast.makeText(this, "微信页面刚刚变化，未能读取；请再点一次。", Toast.LENGTH_LONG).show();
            return;
        } finally { root.recycle(); }
        try {
            String message = InputRules.inboundText(text.toString());
            Intent intent = new Intent(this, MainActivity.class)
                .setAction(Intent.ACTION_SEND)
                .setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, message)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(intent);
        } catch (IllegalArgumentException error) {
            Toast.makeText(this, "当前页面没有可分析的文字。", Toast.LENGTH_LONG).show();
        } catch (RuntimeException error) {
            Toast.makeText(this, "无法打开分析页面；微信文字没有发送。", Toast.LENGTH_LONG).show();
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
        hideMenu();
        if (windows != null && bubble != null) {
            try { windows.removeView(bubble); } catch (RuntimeException ignored) { }
        }
        bubble = null;
        bubbleLayout = null;
    }

    private void hideMenu() {
        if (windows != null && menu != null) {
            try { windows.removeView(menu); } catch (RuntimeException ignored) { }
        }
        menu = null;
        menuLayout = null;
        menuOpen = false;
    }

    private WindowManager.LayoutParams overlay(int width, int height) {
        return new WindowManager.LayoutParams(dp(width), height < 0 ? height : dp(height),
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
            PixelFormat.TRANSLUCENT);
    }

    private Button panelButton(String title, int color) {
        Button button = new Button(this);
        button.setText(title);
        button.setTextSize(14);
        button.setTextColor(Color.rgb(31, 50, 69));
        button.setGravity(Gravity.CENTER);
        button.setBackground(rounded(color, dp(10)));
        button.setClickable(true);
        button.setFocusable(true);
        return button;
    }

    private GradientDrawable circle() {
        GradientDrawable shape = new GradientDrawable();
        shape.setShape(GradientDrawable.OVAL);
        shape.setColor(Color.rgb(246, 202, 75));
        shape.setStroke(dp(2), Color.WHITE);
        return shape;
    }

    private GradientDrawable rounded(int color, int radius) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(color);
        shape.setCornerRadius(radius);
        return shape;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    @Override public void onDestroy() {
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
                if (!moved) toggleMenu();
                return true;
            }
            return event.getAction() == MotionEvent.ACTION_CANCEL;
        }
    }
}
