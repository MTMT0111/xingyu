package com.xingyu.reply.wxapi;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import com.tencent.mm.opensdk.modelbase.BaseReq;
import com.tencent.mm.opensdk.modelbase.BaseResp;
import com.tencent.mm.opensdk.openapi.IWXAPI;
import com.tencent.mm.opensdk.openapi.IWXAPIEventHandler;
import com.tencent.mm.opensdk.openapi.WXAPIFactory;
import com.xingyu.reply.InputRules;
import com.xingyu.reply.WechatSession;

public final class WXEntryActivity extends Activity implements IWXAPIEventHandler {
    private IWXAPI api;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        String appId = getSharedPreferences("xingyu", MODE_PRIVATE).getString("wechatAppId", "");
        if (appId.isEmpty() || !InputRules.validAppId(appId)) { finish(); return; }
        api = WXAPIFactory.createWXAPI(this, appId, true);
        dispatch(getIntent());
    }

    @Override public void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        dispatch(intent);
    }

    private void dispatch(Intent intent) {
        try { if (api != null) api.handleIntent(intent, this); }
        catch (RuntimeException ignored) { /* Reject invalid external callback intents. */ }
        finally { finish(); }
    }

    @Override public void onReq(BaseReq request) { finish(); }
    @Override public void onResp(BaseResp response) { WechatSession.accept(response); finish(); }
    @Override public void onDestroy() { if (api != null) api.detach(); super.onDestroy(); }
}
