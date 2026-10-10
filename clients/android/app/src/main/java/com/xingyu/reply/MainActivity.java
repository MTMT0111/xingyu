package com.xingyu.reply;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.View;
import android.view.WindowInsets;
import android.webkit.CookieManager;
import android.webkit.HttpAuthHandler;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.SslErrorHandler;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import androidx.webkit.JavaScriptReplyProxy;
import androidx.webkit.WebMessageCompat;
import androidx.webkit.WebViewCompat;
import androidx.webkit.WebViewFeature;
import com.tencent.mm.opensdk.modelmsg.SendMessageToWX;
import com.tencent.mm.opensdk.modelmsg.WXMediaMessage;
import com.tencent.mm.opensdk.modelmsg.WXTextObject;
import com.tencent.mm.opensdk.openapi.IWXAPI;
import com.tencent.mm.opensdk.openapi.WXAPIFactory;
import java.io.ByteArrayInputStream;
import java.net.URI;
import java.util.Collections;
import java.util.UUID;
import org.json.JSONException;
import org.json.JSONObject;

/** A small trusted-origin host. The server keeps LLM/Jev credentials; this app never needs them. */
public final class MainActivity extends Activity {
    private static final int FILE_REQUEST = 701;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private SharedPreferences preferences;
    private WebView web;
    private FrameLayout content;
    private ScrollView settings;
    private LinearLayout errorView;
    private TextView errorText;
    private EditText addressInput, appIdInput;
    private ProgressBar progress;
    private volatile String serviceUrl = "";
    private String appId = "", loadingUrl = "";
    private boolean bridgeSupported, listenerAdded, settingsVisible, pageFailed;
    private IWXAPI wechat;
    private boolean registered;
    private ValueCallback<Uri[]> fileCallback;
    private AlertDialog authDialog;
    private JavaScriptReplyProxy pendingProxy;
    private String pendingId, pendingOrigin;
    private Runnable shareTimeout;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        preferences = getSharedPreferences("xingyu", MODE_PRIVATE);
        appId = preferences.getString("wechatAppId", "");
        try { serviceUrl = InputRules.serviceUrl(preferences.getString("serviceUrl", "")); }
        catch (IllegalArgumentException ignored) { serviceUrl = ""; }
        buildInterface();
        configureWechat();
        if (serviceUrl.isEmpty()) showSettings(); else loadService();
        if (Build.VERSION.SDK_INT >= 33) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(0, this::navigateBack);
        }
    }

    private void buildInterface() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.rgb(250, 251, 248));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else {
                view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            }
            return insets;
        });
        LinearLayout toolbar = new LinearLayout(this);
        toolbar.setGravity(android.view.Gravity.CENTER_VERTICAL);
        toolbar.setPadding(dp(16), 0, dp(8), 0);
        TextView title = label("✦ 星语", 20);
        toolbar.addView(title, new LinearLayout.LayoutParams(0, dp(54), 1));
        Button configure = button("设置", this::showSettings);
        toolbar.addView(configure);
        root.addView(toolbar);
        progress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progress.setMax(100);
        root.addView(progress, new LinearLayout.LayoutParams(-1, dp(2)));
        content = new FrameLayout(this);
        root.addView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        createWebView();
        settings = createSettings();
        settings.setVisibility(View.GONE);
        content.addView(settings, new FrameLayout.LayoutParams(-1, -1));
        errorView = new LinearLayout(this);
        errorView.setOrientation(LinearLayout.VERTICAL);
        errorView.setPadding(dp(28), dp(36), dp(28), dp(28));
        errorView.setBackgroundColor(Color.rgb(250, 251, 248));
        errorText = label("", 17);
        errorView.addView(errorText);
        errorView.addView(button("重新加载", this::loadService));
        errorView.addView(button("检查设置", this::showSettings));
        errorView.setVisibility(View.GONE);
        content.addView(errorView, new FrameLayout.LayoutParams(-1, -1));
        setContentView(root);
    }

    private ScrollView createSettings() {
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Color.rgb(250, 251, 248));
        LinearLayout form = new LinearLayout(this);
        form.setOrientation(LinearLayout.VERTICAL);
        form.setPadding(dp(24), dp(28), dp(24), dp(28));
        form.addView(label("把星语带到手机", 25));
        form.addView(label("连接你已部署的服务，生成回复后去微信选人发送。", 15));
        form.addView(label("星语服务地址", 16));
        addressInput = input("HTTPS 根地址", InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        addressInput.setText(serviceUrl);
        form.addView(addressInput);
        form.addView(label("填写根地址，不包含子路径、账号或访问口令。", 13));
        form.addView(label("微信开放平台 AppID（可稍后填写）", 16));
        appIdInput = input("wx 开头的移动应用 AppID", InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        appIdInput.setText(appId);
        form.addView(appIdInput);
        form.addView(label("微信直达需要审核通过的移动应用，以及匹配的包名和签名。暂未配置时，仍可在网页复制回复。", 14));
        form.addView(button("保存并打开", this::saveSettings));
        form.addView(button("返回页面", () -> {
            if (!serviceUrl.isEmpty()) hideSettings();
        }));
        form.addView(label("更换服务地址会重新加载页面；只更改 AppID 会保留当前草稿。服务访问口令仅在连接时输入，不保存在设置里。", 13));
        scroll.addView(form);
        return scroll;
    }

    private void saveSettings() {
        String nextUrl;
        try { nextUrl = InputRules.serviceUrl(addressInput.getText().toString()); }
        catch (IllegalArgumentException error) {
            addressInput.setError("请输入有效 HTTPS 根地址，例如你的已部署域名；不支持子路径。");
            return;
        }
        String nextId = appIdInput.getText().toString().trim();
        if (!InputRules.validAppId(nextId)) {
            appIdInput.setError("AppID 可留空；填写时应为 wx 加 16 位十六进制字符。");
            return;
        }
        boolean newService = !nextUrl.equals(serviceUrl);
        boolean newAppId = !nextId.equals(appId);
        serviceUrl = nextUrl;
        appId = nextId;
        preferences.edit().putString("serviceUrl", serviceUrl).putString("wechatAppId", appId).apply();
        if (newService || newAppId) clearPending();
        configureWechat();
        if (newService || web == null || web.getUrl() == null) loadService(); else hideSettings();
    }

    private void showSettings() {
        addressInput.setText(serviceUrl);
        appIdInput.setText(appId);
        settingsVisible = true;
        settings.setVisibility(View.VISIBLE);
        settings.bringToFront();
        if (web != null) web.setVisibility(View.GONE);
    }

    private void hideSettings() {
        settingsVisible = false;
        settings.setVisibility(View.GONE);
        if (web != null) web.setVisibility(View.VISIBLE);
        if (pageFailed) errorView.bringToFront();
    }

    private void loadService() {
        if (serviceUrl.isEmpty()) { showSettings(); return; }
        clearPending();
        if (web == null) createWebView();
        if (listenerAdded && WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) {
            WebViewCompat.removeWebMessageListener(web, "XingyuWeChat");
        }
        listenerAdded = false;
        if (WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) {
            WebViewCompat.addWebMessageListener(web, "XingyuWeChat", Collections.singleton(InputRules.origin(serviceUrl)),
                (view, message, sourceOrigin, mainFrame, proxy) -> {
                    if (view != web || !mainFrame || !InputRules.sameOrigin(serviceUrl, sourceOrigin.toString()) || !trustedPage()) return;
                    handleBridge(message, proxy);
                });
            listenerAdded = true;
        }
        pageFailed = false;
        errorView.setVisibility(View.GONE);
        hideSettings();
        loadingUrl = serviceUrl;
        web.loadUrl(serviceUrl);
    }

    private void createWebView() {
        web = new WebView(this);
        configureWebView();
        content.addView(web, 0, new FrameLayout.LayoutParams(-1, -1));
    }

    @SuppressWarnings("deprecation")
    private void configureWebView() {
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG);
        WebSettings config = web.getSettings();
        config.setJavaScriptEnabled(true);
        config.setDomStorageEnabled(true);
        config.setAllowFileAccess(false);
        config.setAllowFileAccessFromFileURLs(false);
        config.setAllowUniversalAccessFromFileURLs(false);
        config.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        config.setJavaScriptCanOpenWindowsAutomatically(false);
        config.setSupportMultipleWindows(false);
        config.setSaveFormData(false);
        config.setSavePassword(false);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);
        bridgeSupported = WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return !InputRules.sameOrigin(serviceUrl, request.getUrl().toString());
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return !InputRules.sameOrigin(serviceUrl, url);
            }
            @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                String scheme = request.getUrl().getScheme();
                if ("data".equals(scheme) || "blob".equals(scheme) || InputRules.sameOrigin(serviceUrl, request.getUrl().toString())) return null;
                return new WebResourceResponse("text/plain", "UTF-8", 403, "Blocked", Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
            }
            @Override public void onPageStarted(WebView view, String url, android.graphics.Bitmap icon) {
                if (view != web) return;
                loadingUrl = url;
                clearPending();
                pageFailed = false;
                errorView.setVisibility(View.GONE);
                if (!InputRules.sameOrigin(serviceUrl, url)) {
                    view.stopLoading();
                    showError("已阻止跳转到其他网站。请返回星语服务。");
                }
            }
            @Override public void onReceivedSslError(WebView view, SslErrorHandler ssl, SslError error) {
                ssl.cancel();
                if (view == web) showError("服务的 HTTPS 证书无法验证。请检查服务地址或证书后重试。");
            }
            @Override public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (view == web && request.isForMainFrame()) showError("暂时连接不上服务。请检查网络和服务地址后重试。");
            }
            @Override public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
                if (view == web && request.isForMainFrame() && response.getStatusCode() >= 400) {
                    showError(response.getStatusCode() == 401 ? "服务访问口令未通过，请重新加载并输入口令。" : "服务暂时没有正常响应，请稍后重试。");
                }
            }
            @Override public void onReceivedHttpAuthRequest(WebView view, HttpAuthHandler auth, String host, String realm) {
                if (view == web) promptServicePassword(auth, host); else auth.cancel();
            }
            @Override public boolean onRenderProcessGone(WebView failedView, RenderProcessGoneDetail detail) {
                if (failedView == web) {
                    clearPending();
                    web = null;
                    loadingUrl = "";
                    listenerAdded = false;
                    if (fileCallback != null) { fileCallback.onReceiveValue(null); fileCallback = null; }
                    if (authDialog != null) authDialog.cancel();
                    showError("页面进程已被系统回收，未复制的草稿和附件可能丢失。点击重新加载，安全地打开新页面。");
                }
                content.removeView(failedView);
                failedView.destroy();
                return true;
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override public void onProgressChanged(WebView view, int value) {
                if (view != web) return;
                progress.setProgress(value);
                progress.setVisibility(value == 100 ? View.GONE : View.VISIBLE);
            }
            @Override public boolean onCreateWindow(WebView view, boolean dialog, boolean gesture, android.os.Message result) { return false; }
            @Override public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (view != web || !trustedPage()) { callback.onReceiveValue(null); return true; }
                if (fileCallback != null) fileCallback.onReceiveValue(null);
                fileCallback = callback;
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {"image/jpeg", "image/png", "image/webp", "video/mp4", "video/webm", "video/quicktime"});
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, false);
                intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                try { startActivityForResult(intent, FILE_REQUEST); }
                catch (ActivityNotFoundException error) { fileCallback = null; callback.onReceiveValue(null); }
                return true;
            }
        });
    }

    private void promptServicePassword(HttpAuthHandler auth, String host) {
        if (!canAuthenticate(host)) {
            auth.cancel();
            return;
        }
        if (authDialog != null) { auth.cancel(); return; }
        EditText password = input("服务访问口令", InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        if (Build.VERSION.SDK_INT >= 26) password.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        password.setSaveEnabled(false);
        authDialog = new AlertDialog.Builder(this).setTitle("连接星语服务")
            .setMessage("此服务需要访问口令。用户名为 trial；口令只用于本次连接。")
            .setView(password)
            .setPositiveButton("连接", (dialog, which) -> {
                if (canAuthenticate(host)) auth.proceed("trial", password.getText().toString());
                else auth.cancel();
                password.setText("");
            })
            .setNegativeButton("取消", (dialog, which) -> { auth.cancel(); showError("尚未输入服务访问口令，可重新加载或检查设置。"); })
            .setOnCancelListener(dialog -> { auth.cancel(); showError("连接已取消，可重新加载或检查设置。"); })
            .create();
        authDialog.setOnDismissListener(dialog -> { password.setText(""); authDialog = null; });
        authDialog.show();
    }

    private boolean canAuthenticate(String host) {
        return !serviceUrl.isEmpty() && URI.create(serviceUrl).getHost().equalsIgnoreCase(host)
            && InputRules.sameOrigin(serviceUrl, loadingUrl);
    }

    private void configureWechat() {
        if (wechat != null) wechat.detach();
        wechat = null;
        registered = false;
        if (appId.isEmpty() || !InputRules.validAppId(appId)) return;
        try {
            wechat = WXAPIFactory.createWXAPI(this, appId, true);
            registered = wechat.registerApp(appId);
        } catch (RuntimeException error) { wechat = null; }
    }

    private String availabilityReason() {
        if (serviceUrl.isEmpty() || !InputRules.validAppId(appId)) return "invalid_configuration";
        if (appId.isEmpty()) return "missing_app_id";
        if (!bridgeSupported || wechat == null || !registered) return "sdk_unavailable";
        try { return wechat.isWXAppInstalled() ? "ready" : "wechat_not_installed"; }
        catch (RuntimeException error) { return "sdk_unavailable"; }
    }

    private void handleBridge(WebMessageCompat message, JavaScriptReplyProxy proxy) {
        if (message.getType() != WebMessageCompat.TYPE_STRING) return;
        String data = message.getData();
        if (data == null || data.length() > 65536) return;
        try {
            JSONObject request = new JSONObject(data);
            Object rawId = request.opt("id"), rawType = request.opt("type");
            if (!(rawId instanceof String) || !(rawType instanceof String) || !InputRules.validRequestId((String) rawId)) return;
            String id = (String) rawId, type = (String) rawType;
            if (type.equals("capabilities")) {
                String reason = availabilityReason();
                reply(proxy, object("type", type, "id", id, "available", reason.equals("ready"), "reason", reason, "platform", "android"));
            } else if (type.equals("shareText")) {
                Object rawText = request.opt("text");
                shareText(id, rawText instanceof String ? (String) rawText : null, proxy);
            }
        } catch (JSONException ignored) { /* Malformed bridge input cannot reach the SDK. */ }
    }

    private void shareText(String id, String text, JavaScriptReplyProxy proxy) {
        if (!InputRules.validText(text)) { shareError(proxy, id, "INVALID_TEXT"); return; }
        if (!availabilityReason().equals("ready")) { shareError(proxy, id, "WECHAT_UNAVAILABLE"); return; }
        if (pendingId != null) { shareError(proxy, id, "SHARE_BUSY"); return; }
        WXTextObject textObject = new WXTextObject();
        textObject.text = text;
        WXMediaMessage message = new WXMediaMessage();
        message.mediaObject = textObject;
        SendMessageToWX.Req request = new SendMessageToWX.Req();
        request.transaction = UUID.randomUUID().toString();
        request.message = message;
        request.scene = SendMessageToWX.Req.WXSceneSession;
        if (!request.checkArgs()) { shareError(proxy, id, "INVALID_TEXT"); return; }
        if (!WechatSession.begin(this, id, request.transaction)) { shareError(proxy, id, "SHARE_BUSY"); return; }
        pendingProxy = proxy;
        pendingId = id;
        pendingOrigin = InputRules.origin(serviceUrl);
        try {
            if (!wechat.sendReq(request)) { clearPending(); shareError(proxy, id, "SDK_REJECTED"); return; }
        } catch (RuntimeException error) { clearPending(); shareError(proxy, id, "SDK_ERROR"); return; }
        reply(proxy, object("type", "shareText", "id", id, "status", "handoff"));
        shareTimeout = () -> {
            if (id.equals(pendingId)) {
                if (trustedPending()) reply(pendingProxy, object("type", "shareResult", "id", id, "status", "error", "code", "result_unknown"));
                clearPending();
            }
        };
        handler.postDelayed(shareTimeout, 120000);
    }

    public void onWechatResult(String id, int code) {
        runOnUiThread(() -> {
            if (!id.equals(pendingId)) return;
            if (trustedPending()) reply(pendingProxy, object("type", "shareResult", "id", id,
                "status", code == 0 ? "completed" : code == -2 ? "cancelled" : "error",
                "code", code == 0 ? "OK" : code == -2 ? "USER_CANCELLED" : "SDK_ERROR"));
            clearPending();
        });
    }

    private boolean trustedPending() {
        return pendingProxy != null && trustedPage() && InputRules.origin(serviceUrl).equals(pendingOrigin);
    }

    private void clearPending() {
        if (shareTimeout != null) handler.removeCallbacks(shareTimeout);
        shareTimeout = null;
        WechatSession.clear(this);
        pendingId = null;
        pendingOrigin = null;
        pendingProxy = null;
    }

    private boolean trustedPage() { return web != null && InputRules.sameOrigin(serviceUrl, web.getUrl()); }

    private void shareError(JavaScriptReplyProxy proxy, String id, String code) {
        reply(proxy, object("type", "shareText", "id", id, "status", "error", "code", code));
    }

    private static JSONObject object(Object... entries) {
        JSONObject value = new JSONObject();
        try { for (int index = 0; index < entries.length; index += 2) value.put((String) entries[index], entries[index + 1]); }
        catch (JSONException error) { throw new IllegalArgumentException("INVALID_RESPONSE"); }
        return value;
    }

    private static void reply(JavaScriptReplyProxy proxy, JSONObject response) {
        try {
            if (WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) proxy.postMessage(response.toString());
        }
        catch (RuntimeException ignored) { /* The original document may already be closed. */ }
    }

    private void showError(String message) {
        pageFailed = true;
        errorText.setText(message);
        errorView.setVisibility(View.VISIBLE);
        if (!settingsVisible) errorView.bringToFront();
        progress.setVisibility(View.GONE);
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != FILE_REQUEST || fileCallback == null) return;
        ValueCallback<Uri[]> callback = fileCallback;
        fileCallback = null;
        Uri uri = result == RESULT_OK && data != null ? data.getData() : null;
        if (uri != null && "content".equals(uri.getScheme()) && trustedPage()) callback.onReceiveValue(new Uri[] {uri});
        else callback.onReceiveValue(null);
    }

    @Override public void onBackPressed() { navigateBack(); }
    private void navigateBack() {
        if (settingsVisible && !serviceUrl.isEmpty()) hideSettings();
        else if (!settingsVisible && web != null && web.canGoBack()) web.goBack();
        else moveTaskToBack(true);
    }

    @Override protected void onDestroy() {
        clearPending();
        handler.removeCallbacksAndMessages(null);
        if (authDialog != null) authDialog.cancel();
        if (fileCallback != null) fileCallback.onReceiveValue(null);
        if (wechat != null) wechat.detach();
        if (web != null) web.destroy();
        super.onDestroy();
    }

    private TextView label(String text, int size) {
        TextView label = new TextView(this);
        label.setText(text);
        label.setTextSize(size);
        label.setTextColor(Color.rgb(32, 66, 92));
        label.setGravity(android.view.Gravity.CENTER_VERTICAL);
        label.setPadding(0, dp(8), 0, dp(8));
        return label;
    }

    private EditText input(String hint, int type) {
        EditText field = new EditText(this);
        field.setHint(hint);
        field.setInputType(type);
        field.setSingleLine(true);
        field.setTextSize(16);
        field.setMinHeight(dp(52));
        return field;
    }

    private Button button(String title, Runnable action) {
        Button button = new Button(this);
        button.setText(title);
        button.setAllCaps(false);
        button.setMinHeight(dp(48));
        button.setOnClickListener(view -> action.run());
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
