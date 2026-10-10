package com.xingyu.reply;

import android.os.SystemClock;
import com.tencent.mm.opensdk.modelbase.BaseResp;
import com.tencent.mm.opensdk.modelmsg.SendMessageToWX;
import java.lang.ref.WeakReference;

/** A callback can complete only the one pending transaction created by this app. */
public final class WechatSession {
    private static String transaction;
    private static String requestId;
    private static long expiresAt;
    private static WeakReference<MainActivity> owner = new WeakReference<>(null);

    private WechatSession() {}

    public static boolean begin(MainActivity activity, String id, String token) {
        if (transaction != null && SystemClock.elapsedRealtime() < expiresAt && owner.get() != null) return false;
        transaction = token;
        requestId = id;
        expiresAt = SystemClock.elapsedRealtime() + 120000;
        owner = new WeakReference<>(activity);
        return true;
    }

    public static void clear(MainActivity activity) {
        if (owner.get() == activity) {
            transaction = null;
            requestId = null;
            owner.clear();
        }
    }

    public static void accept(BaseResp response) {
        MainActivity activity = owner.get();
        if (!(response instanceof SendMessageToWX.Resp) || activity == null || transaction == null ||
            !transaction.equals(response.transaction) || SystemClock.elapsedRealtime() >= expiresAt) return;
        String id = requestId;
        clear(activity);
        activity.onWechatResult(id, response.errCode);
    }
}
