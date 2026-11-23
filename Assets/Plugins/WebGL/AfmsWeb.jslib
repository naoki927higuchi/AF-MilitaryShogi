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
  //    element; hidden while in full screen; nothing is added where the browser cannot do it,
  //    and it is removed if a request fails).
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
      // Feature detection only, no OS names (an iPhone reports nothing here, measured 2026-11-24).
      var canFull = !!request && !!(document.fullscreenEnabled || document.webkitFullscreenEnabled);
      if (!canFull || document.querySelector('#fullscreen') || document.querySelector('#afms-fullscreen')) return;
      var button = document.createElement('div');
      button.id = 'afms-fullscreen';
      // Four corner marks drawn as lines (own drawing), not a font character: a symbol character
      // is missing from many devices' fonts.
      button.innerHTML = '<svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="#ffd685" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9V4h5M15 4h5v5M20 15v5h-5M9 20H4v-5"/></svg>';
      button.title = '全画面';
      button.style.cssText = 'position:fixed;right:6px;bottom:6px;width:40px;height:40px;border-radius:8px;' +
        'border:1px solid rgba(255,214,133,.7);background:rgba(11,8,6,.6);align-items:center;justify-content:center;' +
        'cursor:pointer;user-select:none;-webkit-user-select:none;z-index:1000;';
      var isFull = function () { return !!(document.fullscreenElement || document.webkitFullscreenElement); };
      var update = function () { button.style.display = (!canFull || isFull()) ? 'none' : 'flex'; };
      // A request that fails all the same (rejected, or still not full screen a moment later) removes the button.
      var failed = function () { if (!isFull()) { canFull = false; update(); } };
      button.onclick = function () {
        try {
          var p = request.call(root);
          if (p && p.catch) p.catch(failed);
          setTimeout(failed, 1500);
        } catch (e) { failed(); }
      };
      document.addEventListener('fullscreenchange', update);
      document.addEventListener('webkitfullscreenchange', update);
      document.body.appendChild(button);
      update();
    } catch (e) { console.warn('AFMS page preparation failed', e); }
  }
});
