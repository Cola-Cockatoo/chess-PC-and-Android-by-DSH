package com.dsh.chess;

import android.app.Activity;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.ValueCallback;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

/**
 * 国际象棋（手机版）：一个把游戏本体（HTML5 + Canvas）装进 WebView 的壳。
 *
 * 这样做的原因：
 *   1. 游戏逻辑与桌面版一致，改一处两边都受益；
 *   2. WebView 在所有 Android 5.0+ 机型上都可用，兼容性最好；
 *   3. 界面尺寸全靠 CSS/JS 自适应，横竖屏切换不用改代码。
 *
 * 注意：Activity 声明了 configChanges，旋转屏幕不会重建，棋局不会丢。
 */
public class MainActivity extends Activity {

    private static final int BG = 0xFFF6F4F0;

    private WebView webView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        webView = new WebView(this);
        WebSettings s = webView.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setAllowFileAccess(true);
        s.setLoadsImagesAutomatically(true);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setDisplayZoomControls(false);
        s.setUseWideViewPort(false);
        s.setLoadWithOverviewMode(false);
        s.setTextZoom(100);                 // 不跟随系统字体缩放，避免排版错乱
        s.setCacheMode(WebSettings.LOAD_NO_CACHE);
        s.setJavaScriptCanOpenWindowsAutomatically(false);
        s.setMediaPlaybackRequiresUserGesture(false);
        // 显式关掉"本地文件跨源访问"：本应用只加载打包在 assets 里的本地页面，
        // 不需要让它读取其它本地文件（这些开关在 API 16+ 默认就是关的，这里写死以免被 ROM 改默认值）
        try {
            s.setAllowFileAccessFromFileURLs(false);
            s.setAllowUniversalAccessFromFileURLs(false);
            s.setSaveFormData(false);
            s.setGeolocationEnabled(false);
        } catch (Throwable ignored) {
        }
        // 不开 devtools 远程调试（非 debuggable 包默认也是关的，显式关更稳）
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
            try {
                WebView.setWebContentsDebuggingEnabled(false);
            } catch (Throwable ignored) {
            }
        }

        webView.setBackgroundColor(BG);
        webView.setOverScrollMode(View.OVER_SCROLL_NEVER);
        webView.setVerticalScrollBarEnabled(false);
        webView.setHorizontalScrollBarEnabled(false);
        webView.setWebViewClient(new WebViewClient());

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(BG);
        root.addView(webView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);

        // 对局中屏幕常亮
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            getWindow().setStatusBarColor(BG);
            getWindow().setNavigationBarColor(BG);
        }

        webView.loadUrl("file:///android_asset/index.html");
    }

    /**
     * 返回键先交给网页（对局 → 主页），网页说"exit"才退出应用。
     */
    @Override
    public void onBackPressed() {
        if (webView == null) {
            super.onBackPressed();
            return;
        }
        try {
            webView.evaluateJavascript(
                    "(function(){try{return window.__onAndroidBack?window.__onAndroidBack():'exit';}catch(e){return 'exit';}})()",
                    new ValueCallback<String>() {
                        @Override
                        public void onReceiveValue(String value) {
                            if (value == null || value.indexOf("exit") >= 0) {
                                finish();
                            }
                        }
                    });
        } catch (Throwable t) {
            super.onBackPressed();
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (webView != null) webView.onPause();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (webView != null) webView.onResume();
    }

    @Override
    protected void onDestroy() {
        if (webView != null) {
            webView.setWebViewClient(new WebViewClient());
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }
}
