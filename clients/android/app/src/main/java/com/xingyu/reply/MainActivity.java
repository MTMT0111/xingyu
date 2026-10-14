package com.xingyu.reply;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipboardManager;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ComponentName;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.graphics.Color;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
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
import android.widget.Toast;
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
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.util.Collections;
import java.util.HashMap;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

/** A small trusted-origin host. The server keeps LLM/Jev credentials; this app never needs them. */
public final class MainActivity extends Activity {
    public static final String ACTION_QUICK_REPLY = "com.xingyu.reply.action.QUICK_REPLY";
    static final String EXTRA_QUICK_REPLY_TOKEN = "com.xingyu.reply.extra.QUICK_REPLY_TOKEN";
    static final String PREFS_NAME = "xingyu";
    static final String PREF_QUICK_REPLY_TOKEN = "quickReplyToken";
    private static final int FILE_REQUEST = 701;
    private static final String INBOUND_PATH = "/.xingyu-share/";
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService inboundExecutor = Executors.newSingleThreadExecutor();
    private SharedPreferences preferences;
    private WebView web;
    private FrameLayout content;
    private ScrollView settings;
    private LinearLayout errorView;
    private TextView errorText;
    private EditText addressInput, appIdInput;
    private Button pasteButton, copyReturnButton, shareButton, settingsButton, returnButton;
    private ProgressBar progress;
    private volatile String serviceUrl = "";
    private String appId = "", loadingUrl = "";
    private boolean bridgeSupported, settingsVisible, pageFailed;
    private volatile boolean listenerAdded;
    private IWXAPI wechat;
    private boolean registered;
    private ValueCallback<Uri[]> fileCallback;
    private AlertDialog authDialog;
    private JavaScriptReplyProxy pendingProxy;
    private String pendingId, pendingOrigin;
    private Runnable shareTimeout;
    private volatile InboundShare inboundShare;
    private String externalEntryId;
    private volatile long inboundGeneration;
    private boolean pageReady, inboundDispatched, attachmentValidationPending, pendingClipboardImport, destroyed;

    private static final class InboundCandidate {
        final String id, text, declaredMime;
        final Uri uri;
        InboundCandidate(String id, String text, String declaredMime, Uri uri) {
            this.id = id;
            this.text = text;
            this.declaredMime = declaredMime;
            this.uri = uri;
        }
    }

    private static final class InboundShare {
        final String id, entry, text, mime, kind, name, token;
        final Uri uri;
        final long size;
        InboundShare(String id, String entry, String text) {
            this(id, entry, text, null, null, null, null, null, 0);
        }
        InboundShare(String id, String entry, String text, String mime, String kind, String name, String token, Uri uri, long size) {
            this.id = id;
            this.entry = entry;
            this.text = text;
            this.mime = mime;
            this.kind = kind;
            this.name = name;
            this.token = token;
            this.uri = uri;
            this.size = size;
        }
        boolean hasAttachment() { return uri != null; }
    }

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        preferences = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        appId = preferences.getString("wechatAppId", "");
        try { serviceUrl = InputRules.serviceUrl(preferences.getString("serviceUrl", ""), BuildConfig.DEBUG); }
        catch (IllegalArgumentException ignored) { serviceUrl = ""; }
        buildInterface();
        configureWechat();
        boolean restored = restoreInboundState(state);
        if (serviceUrl.isEmpty()) showSettings(); else loadService();
        if (!restored) receiveInboundIntent(getIntent());
        else clearLaunchIntent();
        updateToolbar();
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
        TextView title = label("好好回", 20);
        toolbar.addView(title, new LinearLayout.LayoutParams(0, dp(54), 1));
        returnButton = toolbarButton("返回", this::navigateBack);
        copyReturnButton = toolbarButton("复制返回", this::copyReplyAndReturn);
        pasteButton = toolbarButton("粘贴", this::pasteClipboard);
        shareButton = toolbarButton("发送", this::shareEditedReply);
        settingsButton = toolbarButton("设置", this::showSettings);
        toolbar.addView(returnButton);
        toolbar.addView(copyReturnButton);
        toolbar.addView(pasteButton);
        toolbar.addView(shareButton);
        toolbar.addView(settingsButton);
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
        form.addView(label("把好好回带到手机", 25));
        form.addView(label("连接好好回服务；复制微信消息后，可从快捷磁贴或桌面快捷入口立即开始。", 15));
        if (BuildConfig.DEBUG) {
            form.addView(button("USB 本地调试", () -> {
                addressInput.setText(InputRules.USB_SERVICE_URL);
                saveSettings();
            }));
            form.addView(label("手机连上电脑并开启 USB 调试，在电脑运行本地服务和 adb reverse tcp:4173 tcp:4173 后点此连接。无需公网域名或微信 AppID。", 13));
        }
        form.addView(label("好好回服务地址", 16));
        addressInput = input("HTTPS 根地址", InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        addressInput.setText(serviceUrl);
        form.addView(addressInput);
        form.addView(label("填写根地址，不包含子路径、账号或访问口令。", 13));
        form.addView(label("微信开放平台 AppID（可稍后填写）", 16));
        appIdInput = input("wx 开头的移动应用 AppID", InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        appIdInput.setText(appId);
        form.addView(appIdInput);
        form.addView(label("微信直达需要审核通过的移动应用，以及匹配的包名和签名。暂未配置时，可用系统分享选择微信。", 14));
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
        try { nextUrl = InputRules.serviceUrl(addressInput.getText().toString(), BuildConfig.DEBUG); }
        catch (IllegalArgumentException error) {
            addressInput.setError(BuildConfig.DEBUG
                ? "请输入 HTTPS 根地址；USB 调试仅支持 http://127.0.0.1:4173/。"
                : "请输入有效 HTTPS 根地址，例如你的已部署域名；不支持子路径。");
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
        updateToolbar();
    }

    private void hideSettings() {
        settingsVisible = false;
        settings.setVisibility(View.GONE);
        if (web != null) web.setVisibility(View.VISIBLE);
        if (pageFailed) errorView.bringToFront();
        updateToolbar();
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
            WebViewCompat.addWebMessageListener(web, "XingyuWeChat", Collections.singleton(InputRules.origin(serviceUrl, BuildConfig.DEBUG)),
                (view, message, sourceOrigin, mainFrame, proxy) -> {
                    if (view != web || !mainFrame || !InputRules.sameOrigin(serviceUrl, sourceOrigin.toString(), BuildConfig.DEBUG) || !trustedPage()) return;
                    handleBridge(message, proxy);
                });
            listenerAdded = true;
        }
        pageFailed = false;
        errorView.setVisibility(View.GONE);
        hideSettings();
        loadingUrl = serviceUrl + "?host=android";
        web.loadUrl(loadingUrl);
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
                return !InputRules.sameOrigin(serviceUrl, request.getUrl().toString(), BuildConfig.DEBUG);
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return !InputRules.sameOrigin(serviceUrl, url, BuildConfig.DEBUG);
            }
            @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                WebResourceResponse inbound = interceptInbound(request);
                if (inbound != null) return inbound;
                String scheme = request.getUrl().getScheme();
                if ("data".equals(scheme) || "blob".equals(scheme) || InputRules.sameOrigin(serviceUrl, request.getUrl().toString(), BuildConfig.DEBUG)) return null;
                return new WebResourceResponse("text/plain", "UTF-8", 403, "Blocked", Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
            }
            @Override public void onPageStarted(WebView view, String url, android.graphics.Bitmap icon) {
                if (view != web) return;
                pageReady = false;
                inboundDispatched = false;
                loadingUrl = url;
                clearPending();
                pageFailed = false;
                errorView.setVisibility(View.GONE);
                updateToolbar();
                if (!InputRules.sameOrigin(serviceUrl, url, BuildConfig.DEBUG)) {
                    view.stopLoading();
                    showError("已阻止跳转到其他网站。请返回好好回服务。");
                }
            }
            @Override public void onPageFinished(WebView view, String url) {
                if (view != web || pageFailed || !InputRules.sameOrigin(serviceUrl, url, BuildConfig.DEBUG)) return;
                pageReady = true;
                updateToolbar();
                dispatchInboundShare();
            }
            @Override public void onReceivedSslError(WebView view, SslErrorHandler ssl, SslError error) {
                ssl.cancel();
                if (view == web) showError("服务的 HTTPS 证书无法验证。请检查服务地址或证书后重试。");
            }
            @Override public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (view == web && request.isForMainFrame()) showError(BuildConfig.DEBUG && serviceUrl.equals(InputRules.USB_SERVICE_URL)
                    ? "未连上电脑的好好回服务。请启动本地服务、确认 USB 已授权，再运行 adb reverse tcp:4173 tcp:4173 后重新加载。"
                    : "暂时连接不上服务。请检查网络和服务地址后重试。");
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
                    pageReady = false;
                    inboundDispatched = false;
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

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String action = intent == null ? null : intent.getAction();
        if (ACTION_QUICK_REPLY.equals(action) || Intent.ACTION_SEND.equals(action) || Intent.ACTION_SEND_MULTIPLE.equals(action) || Intent.ACTION_PROCESS_TEXT.equals(action)) {
            receiveInboundIntent(intent);
        } else {
            externalEntryId = null;
            updateToolbar();
        }
    }

    private void receiveInboundIntent(Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        if (ACTION_QUICK_REPLY.equals(action)) {
            pendingClipboardImport = consumeQuickReplyToken(intent);
            clearLaunchIntent();
            if (pendingClipboardImport && hasWindowFocus()) handler.post(this::pasteClipboard);
            else if (!pendingClipboardImport) Toast.makeText(this, "点顶部“粘贴”后开始分析。", Toast.LENGTH_LONG).show();
            return;
        }
        if (!Intent.ACTION_SEND.equals(action) && !Intent.ACTION_SEND_MULTIPLE.equals(action) && !Intent.ACTION_PROCESS_TEXT.equals(action)) return;
        String entryId = UUID.randomUUID().toString();
        clearInboundShare();
        externalEntryId = entryId;
        updateToolbar();
        if (Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            clearLaunchIntent();
            showInboundError("好好回一次只能接收一段文字、一个图片或一个视频。");
            return;
        }
        try {
            if (Intent.ACTION_PROCESS_TEXT.equals(action)) {
                queueInbound(new InboundShare(entryId, "process_text",
                    InputRules.inboundText(intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT))));
                clearLaunchIntent();
                return;
            }
            Uri uri = sharedUri(intent);
            if (InputRules.plainTextMime(intent.getType())) {
                if (uri != null) throw new IllegalArgumentException("INVALID_TEXT_SHARE");
                queueInbound(new InboundShare(entryId, "share",
                    InputRules.inboundText(intent.getCharSequenceExtra(Intent.EXTRA_TEXT))));
                clearLaunchIntent();
                return;
            }
            if (uri == null || !"content".equalsIgnoreCase(uri.getScheme())) throw new IllegalArgumentException("INVALID_URI");
            String caption = intent.hasExtra(Intent.EXTRA_TEXT)
                ? InputRules.inboundText(intent.getCharSequenceExtra(Intent.EXTRA_TEXT)) : null;
            InboundCandidate candidate = new InboundCandidate(entryId, caption, intent.getType(), uri);
            attachmentValidationPending = true;
            long generation = ++inboundGeneration;
            clearLaunchIntent();
            Toast.makeText(this, "正在安全导入分享内容…", Toast.LENGTH_SHORT).show();
            inboundExecutor.execute(() -> validateInboundMedia(candidate, generation));
        } catch (RuntimeException error) {
            clearLaunchIntent();
            showInboundError(inboundErrorMessage(error.getMessage()));
        }
    }

    @SuppressWarnings("deprecation")
    private static Uri extraStream(Intent intent) {
        if (!intent.hasExtra(Intent.EXTRA_STREAM)) return null;
        if (Build.VERSION.SDK_INT >= 33) return intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri.class);
        Object value = intent.getParcelableExtra(Intent.EXTRA_STREAM);
        if (value == null || value instanceof Uri) return (Uri) value;
        throw new IllegalArgumentException("INVALID_URI");
    }

    private static Uri sharedUri(Intent intent) {
        Uri extra = extraStream(intent), clipped = null;
        ClipData clip = intent.getClipData();
        if (clip != null) {
            if (clip.getItemCount() != 1) throw new IllegalArgumentException("MULTIPLE_ITEMS");
            clipped = clip.getItemAt(0).getUri();
        }
        if (extra != null && clipped != null && !extra.equals(clipped)) throw new IllegalArgumentException("MULTIPLE_ITEMS");
        return extra != null ? extra : clipped;
    }

    private void validateInboundMedia(InboundCandidate candidate, long generation) {
        try {
            String resolved = getContentResolver().getType(candidate.uri);
            String mime = InputRules.mediaMime(candidate.declaredMime, resolved);
            long max = InputRules.maxMediaBytes(mime), declaredSize = -1;
            String displayName = null;
            try (Cursor cursor = getContentResolver().query(candidate.uri,
                new String[] {OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
                if (cursor != null && cursor.moveToFirst()) {
                    int nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    int sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE);
                    if (nameIndex >= 0 && !cursor.isNull(nameIndex)) displayName = cursor.getString(nameIndex);
                    if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) declaredSize = cursor.getLong(sizeIndex);
                }
            }
            if (declaredSize < 0) {
                try (AssetFileDescriptor descriptor = getContentResolver().openAssetFileDescriptor(candidate.uri, "r")) {
                    if (descriptor != null) declaredSize = descriptor.getLength();
                }
            }
            if (declaredSize == 0) throw new IllegalArgumentException("EMPTY_MEDIA");
            if (declaredSize > max) throw new IllegalArgumentException("MEDIA_TOO_LARGE");
            long actualSize = 0;
            byte[] buffer = new byte[64 * 1024];
            try (InputStream stream = getContentResolver().openInputStream(candidate.uri)) {
                if (stream == null) throw new IOException("missing stream");
                for (int count; (count = stream.read(buffer)) != -1;) {
                    if (generation != inboundGeneration) throw new IllegalArgumentException("CANCELLED");
                    actualSize += count;
                    if (actualSize > max) throw new IllegalArgumentException("MEDIA_TOO_LARGE");
                }
            }
            if (actualSize == 0) throw new IllegalArgumentException("EMPTY_MEDIA");
            InboundShare value = new InboundShare(candidate.id, "share", candidate.text, mime, InputRules.mediaKind(mime),
                InputRules.safeDisplayName(displayName, mime), UUID.randomUUID().toString(), candidate.uri, actualSize);
            handler.post(() -> {
                if (destroyed || generation != inboundGeneration) return;
                attachmentValidationPending = false;
                queueInbound(value);
            });
        } catch (IllegalArgumentException error) {
            inboundValidationFailed(generation, inboundErrorMessage(error.getMessage()));
        } catch (IOException | RuntimeException error) {
            inboundValidationFailed(generation, "无法读取这个分享文件，请回到原应用重新分享。");
        }
    }

    private void inboundValidationFailed(long generation, String message) {
        handler.post(() -> {
            if (destroyed || generation != inboundGeneration) return;
            attachmentValidationPending = false;
            showInboundError(message);
        });
    }

    private void queueInbound(InboundShare value) {
        ++inboundGeneration;
        attachmentValidationPending = false;
        inboundShare = value;
        externalEntryId = value.id;
        updateToolbar();
        inboundDispatched = false;
        if (serviceUrl.isEmpty()) {
            showSettings();
            Toast.makeText(this, "分享内容已暂存，请先连接好好回服务。", Toast.LENGTH_LONG).show();
        } else {
            hideSettings();
            dispatchInboundShare();
        }
    }

    private void dispatchInboundShare() {
        InboundShare value = inboundShare;
        if (value == null || inboundDispatched || !pageReady || !trustedPage()) return;
        JSONObject detail = object("schemaVersion", 1, "type", "inboundShare", "id", value.id, "entry", value.entry);
        try {
            if (value.text != null) detail.put("text", value.text);
            if (value.hasAttachment()) detail.put("attachment", object("url", InputRules.origin(serviceUrl, BuildConfig.DEBUG) + INBOUND_PATH + value.token,
                "name", value.name, "mime", value.mime, "size", value.size, "kind", value.kind));
        } catch (JSONException error) { throw new IllegalArgumentException("INVALID_INBOUND_SHARE"); }
        String script = "window.dispatchEvent(new CustomEvent('xingyu-inbound-share',{detail:JSON.parse(" +
            JSONObject.quote(detail.toString()) + ")}));";
        inboundDispatched = true;
        try {
            web.evaluateJavascript(script, ignored -> {
                if (!listenerAdded && !value.hasAttachment()) clearInboundShare(value.id);
            });
            if (!listenerAdded && value.hasAttachment()) {
                handler.postDelayed(() -> clearInboundShare(value.id), 5 * 60 * 1000L);
            }
        }
        catch (RuntimeException error) {
            inboundDispatched = false;
            showInboundError("暂时无法把分享内容交给页面，请重新加载后再试。");
        }
    }

    private WebResourceResponse interceptInbound(WebResourceRequest request) {
        String path = request.getUrl().getPath();
        if (!InputRules.sameOrigin(serviceUrl, request.getUrl().toString(), BuildConfig.DEBUG) ||
            path == null || !path.startsWith(INBOUND_PATH)) return null;
        InboundShare value = inboundShare;
        String token = path.substring(INBOUND_PATH.length());
        if (!"GET".equals(request.getMethod()) || request.getUrl().getQuery() != null || value == null ||
            !value.hasAttachment() || !token.equals(value.token)) return inboundResponse(404, "Not Found", "text/plain", 0, new byte[0]);
        try {
            InputStream stream = getContentResolver().openInputStream(value.uri);
            if (stream == null) return inboundResponse(410, "Gone", "text/plain", 0, new byte[0]);
            Runnable cleanup = listenerAdded ? null : () -> handler.post(() -> clearInboundShare(value.id));
            return inboundResponse(200, "OK", value.mime, value.size, new BoundedInputStream(stream, value.size, cleanup));
        } catch (IOException | RuntimeException error) {
            return inboundResponse(410, "Gone", "text/plain", 0, new byte[0]);
        }
    }

    private static WebResourceResponse inboundResponse(int status, String reason, String mime, long size, InputStream stream) {
        HashMap<String, String> headers = new HashMap<>();
        headers.put("Cache-Control", "no-store");
        headers.put("X-Content-Type-Options", "nosniff");
        if (size > 0) headers.put("Content-Length", Long.toString(size));
        return new WebResourceResponse(mime, null, status, reason, headers, stream);
    }

    private static WebResourceResponse inboundResponse(int status, String reason, String mime, long size, byte[] body) {
        return inboundResponse(status, reason, mime, size, new ByteArrayInputStream(body));
    }

    private static final class BoundedInputStream extends FilterInputStream {
        private long remaining;
        private final Runnable closeAction;
        private boolean closed;
        BoundedInputStream(InputStream stream, long size, Runnable closeAction) {
            super(stream);
            remaining = size;
            this.closeAction = closeAction;
        }
        @Override public int read() throws IOException {
            if (remaining == 0) {
                if (super.read() != -1) throw new IOException("content changed");
                return -1;
            }
            int value = super.read();
            if (value != -1) remaining--;
            return value;
        }
        @Override public int read(byte[] buffer, int offset, int length) throws IOException {
            if (length == 0) return 0;
            if (remaining == 0) return read() == -1 ? -1 : 0;
            int count = super.read(buffer, offset, (int) Math.min(length, remaining));
            if (count > 0) remaining -= count;
            return count;
        }
        @Override public void close() throws IOException {
            if (closed) return;
            closed = true;
            try { super.close(); }
            finally { if (closeAction != null) closeAction.run(); }
        }
    }

    private void clearInboundShare() {
        ++inboundGeneration;
        attachmentValidationPending = false;
        inboundShare = null;
        inboundDispatched = false;
    }

    private void clearInboundShare(String id) {
        InboundShare value = inboundShare;
        if (value != null && value.id.equals(id)) clearInboundShare();
    }

    private void clearLaunchIntent() {
        setIntent(new Intent(this, MainActivity.class).setAction(Intent.ACTION_MAIN));
    }

    private static String inboundErrorMessage(String code) {
        if ("MISSING_TEXT".equals(code) || "INVALID_TEXT".equals(code) || "INVALID_TEXT_SHARE".equals(code))
            return "这段分享文字为空、过长或格式异常，请缩短后重试。";
        if ("MULTIPLE_ITEMS".equals(code)) return "好好回一次只能接收一个图片或视频。";
        if ("EMPTY_MEDIA".equals(code)) return "这个分享文件是空的，请重新选择。";
        if ("MEDIA_TOO_LARGE".equals(code)) return "图片不能超过 4 MB，视频不能超过 20 MB，请压缩后重试。";
        if ("UNSUPPORTED_MEDIA".equals(code)) return "目前只支持 JPG、PNG、WebP 图片和 MP4、WebM、MOV 视频。";
        return "无法读取这个分享内容，请回到原应用重新分享。";
    }

    private void showInboundError(String message) {
        if (destroyed || isFinishing()) return;
        new AlertDialog.Builder(this).setTitle("无法导入分享").setMessage(message)
            .setPositiveButton("知道了", null).show();
    }

    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if (focused && pendingClipboardImport) pasteClipboard();
    }

    private boolean consumeQuickReplyToken(Intent intent) {
        String provided = intent.getStringExtra(EXTRA_QUICK_REPLY_TOKEN);
        String expected = preferences.getString(PREF_QUICK_REPLY_TOKEN, null);
        if (!InputRules.quickReplyTokenMatches(provided, expected)) return false;
        return preferences.edit().remove(PREF_QUICK_REPLY_TOKEN).commit();
    }

    private void pasteClipboard() {
        pendingClipboardImport = false;
        try {
            ClipboardManager clipboard = getSystemService(ClipboardManager.class);
            ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
            CharSequence text = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).getText() : null;
            queueInbound(new InboundShare(UUID.randomUUID().toString(), "clipboard", InputRules.inboundText(text)));
            clearLaunchIntent();
        } catch (RuntimeException error) {
            new AlertDialog.Builder(this).setTitle("剪贴板里没有可用文字")
                .setMessage("请先在微信复制一段消息，再点“粘贴”或快捷磁贴。好好回不会在后台监听剪贴板。")
                .setPositiveButton("知道了", null).show();
        }
    }

    private void shareEditedReply() {
        readEditedReply(text -> {
            Intent send = new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text);
            Intent chooser = Intent.createChooser(send, "选择发送到哪里");
            chooser.putExtra(Intent.EXTRA_EXCLUDE_COMPONENTS, new ComponentName[] {getComponentName()});
            try { startActivity(chooser); }
            catch (ActivityNotFoundException error) { Toast.makeText(this, "系统里没有可用的分享应用。", Toast.LENGTH_LONG).show(); }
        });
    }

    private void copyReplyAndReturn() {
        readEditedReply(text -> {
            try {
                ClipboardManager clipboard = getSystemService(ClipboardManager.class);
                if (clipboard == null) throw new IllegalStateException("clipboard unavailable");
                clipboard.setPrimaryClip(ClipData.newPlainText("好好回回复", text));
                externalEntryId = null;
                finish();
            } catch (RuntimeException error) {
                Toast.makeText(this, "复制失败，请保留页面后重试。", Toast.LENGTH_LONG).show();
            }
        });
    }

    private void readEditedReply(ValueCallback<String> callback) {
        if (!pageReady || settingsVisible || pageFailed || !trustedPage()) {
            Toast.makeText(this, "页面还没准备好，请稍后再试。", Toast.LENGTH_SHORT).show();
            return;
        }
        web.evaluateJavascript("(()=>{const e=document.getElementById('reply-text');return e?e.value:''})()", value -> {
            try {
                Object decoded = new JSONTokener(value).nextValue();
                if (!(decoded instanceof String) || !InputRules.validText((String) decoded)) throw new JSONException("invalid reply");
                callback.onReceiveValue((String) decoded);
            } catch (JSONException | RuntimeException error) {
                Toast.makeText(this, "请先生成并确认一条回复。", Toast.LENGTH_SHORT).show();
            }
        });
    }

    private boolean restoreInboundState(Bundle state) {
        if (state == null) return false;
        externalEntryId = state.getString("inbound.entryId");
        if (state.getBoolean("inbound.contentLost")) {
            handler.post(() -> showInboundError("系统回收了页面后，无法继续读取原分享内容，请回到原应用重新分享。"));
        }
        return externalEntryId != null || state.getBoolean("inbound.contentLost");
    }

    @Override protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putString("inbound.entryId", externalEntryId);
        InboundShare value = inboundShare;
        state.putBoolean("inbound.contentLost", attachmentValidationPending || value != null);
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
        authDialog = new AlertDialog.Builder(this).setTitle("连接好好回服务")
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
            && InputRules.sameOrigin(serviceUrl, loadingUrl, BuildConfig.DEBUG);
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
            } else if (type.equals("ackInboundShare")) {
                Object rawAction = request.opt("action");
                if (inboundShare != null && id.equals(inboundShare.id) && rawAction instanceof String &&
                    (((String) rawAction).equals("accepted") || ((String) rawAction).equals("dismissed"))) clearInboundShare();
            } else if (type.equals("finishShareEntry") && id.equals(externalEntryId)) {
                externalEntryId = null;
                finish();
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
        pendingOrigin = InputRules.origin(serviceUrl, BuildConfig.DEBUG);
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
        return pendingProxy != null && trustedPage() && InputRules.origin(serviceUrl, BuildConfig.DEBUG).equals(pendingOrigin);
    }

    private void clearPending() {
        if (shareTimeout != null) handler.removeCallbacks(shareTimeout);
        shareTimeout = null;
        WechatSession.clear(this);
        pendingId = null;
        pendingOrigin = null;
        pendingProxy = null;
    }

    private boolean trustedPage() { return web != null && InputRules.sameOrigin(serviceUrl, web.getUrl(), BuildConfig.DEBUG); }

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
        updateToolbar();
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
        else if (externalEntryId != null) { externalEntryId = null; finish(); }
        else if (!settingsVisible && web != null && web.canGoBack()) web.goBack();
        else moveTaskToBack(true);
    }

    private void updateToolbar() {
        if (pasteButton == null) return;
        boolean external = externalEntryId != null;
        returnButton.setVisibility(external ? View.VISIBLE : View.GONE);
        copyReturnButton.setVisibility(external ? View.VISIBLE : View.GONE);
        pasteButton.setVisibility(external ? View.GONE : View.VISIBLE);
        settingsButton.setVisibility(external ? View.GONE : View.VISIBLE);
        shareButton.setEnabled(pageReady && !settingsVisible && !pageFailed);
    }

    @Override protected void onDestroy() {
        destroyed = true;
        inboundExecutor.shutdownNow();
        clearInboundShare();
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

    private Button toolbarButton(String title, Runnable action) {
        Button button = button(title, action);
        button.setMinHeight(dp(44));
        button.setMinWidth(0);
        button.setMinimumWidth(0);
        button.setPadding(dp(10), 0, dp(10), 0);
        button.setTextSize(13);
        button.setTextColor(Color.rgb(54, 85, 156));
        button.setBackgroundColor(Color.TRANSPARENT);
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
