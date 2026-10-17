package com.xingyu.reply;

import android.annotation.SuppressLint;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;
import android.widget.Toast;
import java.util.UUID;

/** Opens the foreground app; clipboard access remains an explicit user action. */
public final class QuickReplyTileService extends TileService {
    @Override public void onStartListening() {
        Tile tile = getQsTile();
        if (tile == null) return;
        tile.setLabel(getString(R.string.tile_label));
        tile.setState(Tile.STATE_ACTIVE);
        tile.updateTile();
    }

    @SuppressLint("StartActivityAndCollapseDeprecated")
    @SuppressWarnings("deprecation")
    @Override public void onClick() {
        super.onClick();
        String token = UUID.randomUUID().toString();
        boolean saved = getSharedPreferences(MainActivity.PREFS_NAME, MODE_PRIVATE).edit()
            .putString(MainActivity.PREF_QUICK_REPLY_TOKEN, token).commit();
        if (!saved) {
            Toast.makeText(this, "暂时无法打开，请直接进入言外。", Toast.LENGTH_LONG).show();
            return;
        }
        Intent intent = new Intent(this, MainActivity.class)
            .setAction(MainActivity.ACTION_QUICK_REPLY)
            .putExtra(MainActivity.EXTRA_QUICK_REPLY_TOKEN, token)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        if (Build.VERSION.SDK_INT >= 34) {
            PendingIntent pending = PendingIntent.getActivity(this, 0, intent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            startActivityAndCollapse(pending);
        } else {
            startActivityAndCollapse(intent);
        }
    }
}
