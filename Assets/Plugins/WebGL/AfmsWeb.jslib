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
  }
});
