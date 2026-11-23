// Web (WebGL) only: bridge to the browser.
mergeInto(LibraryManager.library, {
  // Copies the files written below /idbfs (presets.txt, settings.txt) to IndexedDB.
  // Done from the game so that it works on any hosting page (itch.io, unityroom): the page's
  // autoSyncPersistentDataPath option is not relied on. A sync requested while one is running
  // is queued, so the last write always reaches the storage.
  AfmsSyncFiles: function () {
    try {
      if (typeof FS === 'undefined' || !FS.syncfs) return;
      var state = Module.afmsSync || (Module.afmsSync = { running: false, again: false });
      if (state.running) { state.again = true; return; }
      var run = function () {
        state.running = true;
        FS.syncfs(false, function (err) {
          if (err) console.warn('AFMS syncfs failed', err);
          state.running = false;
          if (state.again) { state.again = false; run(); }
        });
      };
      run();
    } catch (e) { console.warn('AFMS syncfs failed', e); }
  },

  // Phones on a hosting page we do not control (measured on unityroom, 2026-11-23): the page has no
  // viewport meta (so the browser lays it out 980 px wide and shrinks it), tells Unity that the
  // device pixel ratio is 1, and offers no way into full screen. The game repairs that itself:
  //  - adds a viewport meta when the page has none;
  //  - lets Unity read the real device pixel ratio again (Unity asks Module.devicePixelRatio first);
  //  - adds a full-screen button when the page has none (it needs a real tap, so it is a DOM
  //    element; hidden while in full screen; nothing is added where the browser cannot do it).
  // Only on phones and tablets; a desktop browser's page is left alone. Our own page already has
  // all three, so nothing changes there.
  AfmsPreparePage: function () {
    try {
      if (!/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) return;
      if (!document.querySelector('meta[name="viewport"]')) {
        var meta = document.createElement('meta');
        meta.name = 'viewport';
        meta.content = 'width=device-width, height=device-height, initial-scale=1.0, user-scalable=no';
        document.head.appendChild(meta);
      }
      if (Module.devicePixelRatio) Module.devicePixelRatio = 0;
      var canvas = Module.canvas;
      if (canvas) canvas.style.touchAction = 'none';
      var root = document.documentElement;
      var request = root.requestFullscreen || root.webkitRequestFullscreen;
      // iPhone has no full screen for a page: no button there (it would do nothing).
      var canFull = !!(document.fullscreenEnabled || document.webkitFullscreenEnabled) && !/iPhone|iPod/i.test(navigator.userAgent);
      if (!request || !canFull || document.querySelector('#fullscreen') || document.querySelector('#afms-fullscreen')) return;
      var button = document.createElement('div');
      button.id = 'afms-fullscreen';
      button.textContent = '⛶';
      button.title = '全画面';
      button.style.cssText = 'position:fixed;right:6px;bottom:6px;width:40px;height:40px;border-radius:8px;' +
        'border:1px solid rgba(255,214,133,.7);background:rgba(11,8,6,.6);color:#ffd685;font-size:22px;' +
        'line-height:38px;text-align:center;cursor:pointer;user-select:none;-webkit-user-select:none;z-index:1000;';
      var isFull = function () { return !!(document.fullscreenElement || document.webkitFullscreenElement); };
      var update = function () { button.style.display = isFull() ? 'none' : 'block'; };
      button.onclick = function () { try { request.call(root); } catch (e) { console.warn('AFMS fullscreen failed', e); } };
      document.addEventListener('fullscreenchange', update);
      document.addEventListener('webkitfullscreenchange', update);
      document.body.appendChild(button);
      update();
    } catch (e) { console.warn('AFMS page preparation failed', e); }
  }
});
