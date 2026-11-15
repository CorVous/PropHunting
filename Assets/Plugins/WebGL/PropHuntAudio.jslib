// Sound for the browser build, played with the Web Audio API.
// Why not Unity's own audio: the Linux editor cannot encode audio clips for WebGL without ffmpeg, and Unity's
// run-time loader refuses .ogg on WebGL ("Streaming of 'ogg' on this platform is not supported"). The browser
// decodes the packs' .ogg files itself, so they are fetched from StreamingAssets and played here.
// Positional sounds get their gain and stereo pan from C# (AudioHub.cs) at the moment they start.
mergeInto(LibraryManager.library, {

  PH_AudioInit: function () {
    if (window.__phAudio) return;
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) { window.__phAudio = null; return; }
    var ctx = new AC();
    var master = ctx.createGain();
    master.connect(ctx.destination);
    window.__phAudio = { ctx: ctx, master: master, buffers: {}, loaded: 0, failed: 0 };
    // browsers start an audio context suspended until the user does something
    var resume = function () { if (ctx.state !== 'running') ctx.resume(); };
    ['mousedown', 'pointerdown', 'click', 'keydown', 'touchend'].forEach(function (n) { window.addEventListener(n, resume, true); });
  },

  PH_AudioLoad: function (namePtr, urlPtr) {
    var a = window.__phAudio;
    if (!a) return;
    var name = UTF8ToString(namePtr), url = UTF8ToString(urlPtr);
    fetch(url).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.arrayBuffer();
    }).then(function (data) {
      return new Promise(function (resolve, reject) { a.ctx.decodeAudioData(data, resolve, reject); });
    }).then(function (buffer) {
      a.buffers[name] = buffer;
      a.loaded++;
    }).catch(function (e) {
      a.failed++;
      console.warn('[prophunt] sound ' + name + ' did not load: ' + e);
    });
  },

  PH_AudioPlay: function (namePtr, gain, pan, rate) {
    var a = window.__phAudio;
    if (!a) return 0;
    var buffer = a.buffers[UTF8ToString(namePtr)];
    if (!buffer) return 0;
    if (a.ctx.state !== 'running') { a.ctx.resume(); return 0; }   // nothing queues up while the browser holds audio back
    var src = a.ctx.createBufferSource();
    src.buffer = buffer;
    src.playbackRate.value = rate;
    var g = a.ctx.createGain();
    g.gain.value = gain;
    src.connect(g);
    if (a.ctx.createStereoPanner) {
      var p = a.ctx.createStereoPanner();
      p.pan.value = pan;
      g.connect(p);
      p.connect(a.master);
    } else {
      g.connect(a.master);
    }
    src.start(0);
    return 1;
  },

  PH_AudioVolume: function (v) {
    if (window.__phAudio) window.__phAudio.master.gain.value = v;
  },

  PH_AudioLoaded: function () {
    return window.__phAudio ? window.__phAudio.loaded : 0;
  },

  PH_AudioFailed: function () {
    return window.__phAudio ? window.__phAudio.failed : -1;
  },

});
