package com.xingyu.reply;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.inputmethodservice.InputMethodService;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

/** A user-invoked keyboard bridge: clipboard in, reviewed reply out. */
public final class ReplyInputMethodService extends InputMethodService {
    private TextView status;

    @Override public View onCreateInputView() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(16), dp(14), dp(16), dp(14));
        root.setBackgroundColor(Color.rgb(248, 248, 244));

        LinearLayout heading = new LinearLayout(this);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = text("言外", 18, Color.rgb(25, 31, 29));
        title.setTypeface(title.getTypeface(), android.graphics.Typeface.BOLD);
        heading.addView(title, new LinearLayout.LayoutParams(0, dp(42), 1));
        Button switcher = button("切换键盘", false, view -> showKeyboardPicker());
        heading.addView(switcher, new LinearLayout.LayoutParams(dp(96), dp(42)));
        root.addView(heading);

        status = text("等待一段已复制的消息", 13, Color.rgb(100, 112, 106));
        root.addView(status, new LinearLayout.LayoutParams(-1, dp(38)));

        LinearLayout actions = new LinearLayout(this);
        actions.setGravity(Gravity.CENTER_VERTICAL);
        Button analyze = button("分析剪贴板", false, view -> analyzeClipboard());
        Button insert = button("插入回复", true, view -> insertClipboard());
        actions.addView(analyze, new LinearLayout.LayoutParams(0, dp(52), 1));
        LinearLayout.LayoutParams insertLayout = new LinearLayout.LayoutParams(0, dp(52), 1);
        insertLayout.setMarginStart(dp(10));
        actions.addView(insert, insertLayout);
        root.addView(actions);
        return root;
    }

    @Override public void onStartInputView(android.view.inputmethod.EditorInfo info, boolean restarting) {
        super.onStartInputView(info, restarting);
        if (status != null) status.setText("等待一段已复制的消息");
    }

    private void analyzeClipboard() {
        CharSequence value = clipboardText();
        if (value == null) {
            status.setText("剪贴板里没有可分析的文字");
            return;
        }
        try {
            String text = InputRules.inboundText(value);
            Intent intent = new Intent(this, MainActivity.class)
                .setAction(Intent.ACTION_SEND)
                .setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, text)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(intent);
        } catch (IllegalArgumentException error) {
            status.setText("这段文字为空、过长或格式异常");
        }
    }

    private void insertClipboard() {
        CharSequence value = clipboardText();
        InputConnection connection = getCurrentInputConnection();
        if (value == null || connection == null) {
            status.setText("没有可插入的回复");
            return;
        }
        String text = value.toString();
        if (!InputRules.validText(text)) {
            status.setText("这段回复为空、过长或格式异常");
            return;
        }
        if (connection.commitText(text, 1)) status.setText("已放入当前聊天框");
        else status.setText("当前输入框暂时不能写入");
    }

    private CharSequence clipboardText() {
        try {
            ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
            if (clip == null || clip.getItemCount() == 0) return null;
            CharSequence value = clip.getItemAt(0).coerceToText(this);
            return value == null || value.toString().trim().isEmpty() ? null : value;
        } catch (RuntimeException error) {
            return null;
        }
    }

    private void showKeyboardPicker() {
        InputMethodManager manager = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        if (manager != null) manager.showInputMethodPicker();
        else Toast.makeText(this, "请在系统设置中切换输入法。", Toast.LENGTH_SHORT).show();
    }

    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(color);
        view.setGravity(Gravity.CENTER_VERTICAL);
        return view;
    }

    private Button button(String title, boolean primary, View.OnClickListener action) {
        Button button = new Button(this);
        button.setText(title);
        button.setAllCaps(false);
        button.setTextSize(14);
        button.setTextColor(primary ? Color.WHITE : Color.rgb(40, 89, 72));
        button.setPadding(dp(12), 0, dp(12), 0);
        GradientDrawable background = new GradientDrawable();
        background.setColor(primary ? Color.rgb(40, 105, 82) : Color.WHITE);
        background.setCornerRadius(dp(8));
        background.setStroke(dp(1), primary ? Color.rgb(40, 105, 82) : Color.rgb(205, 218, 211));
        button.setBackground(background);
        button.setOnClickListener(action);
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
