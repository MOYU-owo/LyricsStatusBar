(() => {
  "use strict";
  const VERSION = 1;
  const PLUGIN_VERSION = "1.1.1";
  const TRACK_RESEND_MS = 5000;
  let currentTrackId = "";
  let currentTrack = null;
  let loadSequence = 0;
  let lastProgressSentAt = 0;
  let lastTrackSentAt = 0;
  let lastTrackRetryAt = 0;
  let messageSequence = 0;
  let fileBridgeReady = null;
  let fileBridgeQueue = Promise.resolve();

  function ensureFileBridgeDirectory() {
    if (fileBridgeReady) {
      return fileBridgeReady;
    }
    fileBridgeReady = (async () => {
      try {
        if (window.betterncm_native && betterncm_native.fs && typeof betterncm_native.fs.mkdir === "function") {
          betterncm_native.fs.mkdir("./LyricsStatusBarBridge");
          return;
        }
      } catch (_) {
      }
      if (window.betterncm && betterncm.fs && typeof betterncm.fs.mkdir === "function") {
        await betterncm.fs.mkdir("./LyricsStatusBarBridge");
      }
    })();
    return fileBridgeReady;
  }

  async function writeBridgeFile(path, text) {
    if (window.betterncm_native && betterncm_native.fs && typeof betterncm_native.fs.writeFileText === "function") {
      betterncm_native.fs.writeFileText(path, text);
      return;
    }
    if (window.betterncm && betterncm.fs && typeof betterncm.fs.writeFileText === "function") {
      await betterncm.fs.writeFileText(path, text);
      return;
    }
    if (window.betterncm && betterncm.fs && typeof betterncm.fs.writeFile === "function") {
      await betterncm.fs.writeFile(path, new Blob([text], { type: "application/json" }));
    }
  }

  async function writeFileBridge(message, text) {
    await ensureFileBridgeDirectory();
    await writeBridgeFile("./LyricsStatusBarBridge/latest.json", text);
    if (/^(hello|track|progress|clear)$/.test(message.type || "")) {
      await writeBridgeFile("./LyricsStatusBarBridge/" + message.type + ".json", text);
    }
  }

  function send(message) {
    const envelope = { version: VERSION, _seq: ++messageSequence, _time: Date.now(), ...message };
    const text = JSON.stringify(envelope);
    try {
      if (window.betterncm_native && betterncm_native.native_plugin) {
        betterncm_native.native_plugin.call("lyrics_statusbar.send", [text]);
      }
    } catch (error) {
      console.error("[Lyrics StatusBar] native bridge send failed", error);
    }
    fileBridgeQueue = fileBridgeQueue
      .then(() => writeFileBridge(envelope, text))
      .catch(error => console.error("[Lyrics StatusBar] file bridge send failed", error));
  }
  function clear(reason) {
    currentTrack = null;
    send({ type: "clear", reason });
  }

  function hide(reason) {
    send({ type: "clear", reason });
  }

  function parseLrc(source) {
    if (!source || typeof source !== "string") {
      return [];
    }
    const offsetMatch = source.match(/^\[offset:([+-]?\d+)\]$/im);
    const offset = offsetMatch ? Number(offsetMatch[1]) : 0;
    const result = [];
    for (const rawLine of source.replace(/\r/g, "").split("\n")) {
      const timestamps = [...rawLine.matchAll(/\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]/g)];
      if (timestamps.length === 0) {
        continue;
      }
      const last = timestamps[timestamps.length - 1];
      const text = rawLine.slice(last.index + last[0].length).trim();
      if (!text) {
        continue;
      }
      for (const timestamp of timestamps) {
        const fraction = timestamp[3] || "";
        const fractionMs = fraction.length === 1
          ? Number(fraction) * 100
          : fraction.length === 2
            ? Number(fraction) * 10
            : Number(fraction.slice(0, 3) || 0);
        result.push({
          timeMs: Math.max(0, Number(timestamp[1]) * 60000 + Number(timestamp[2]) * 1000 + fractionMs + offset),
          text
        });
      }
    }
    result.sort((left, right) => left.timeMs - right.timeMs || left.text.localeCompare(right.text));
    return result.filter((line, index) =>
      index === 0 ||
      line.timeMs !== result[index - 1].timeMs ||
      line.text !== result[index - 1].text
    );
  }

  async function fetchJson(url) {
    const response = await fetch(url, { credentials: "include" });
    if (!response.ok) {
      throw new Error("HTTP " + response.status);
    }
    return response.json();
  }

  function getNestedValue(source, path) {
    let current = source;
    for (const part of path) {
      if (!current || typeof current !== "object" || !(part in current)) {
        return "";
      }
      current = current[part];
    }
    return current;
  }

  function normalizeTrackId(value) {
    if (typeof value === "number" && Number.isFinite(value) && value > 10000) {
      return String(Math.trunc(value));
    }
    if (typeof value !== "string") {
      return "";
    }
    const match = value.match(/(?:^|[^\d])(\d{5,})(?:_|\b|$)/);
    return match ? match[1] : "";
  }

  function extractTrackId(...args) {
    const preferredPaths = [["id"], ["trackId"], ["songId"], ["playId"], ["resourceId"], ["from", "id"], ["data", "id"], ["song", "id"], ["track", "id"], ["info", "id"], ["info", "trackId"], ["info", "songId"]];
    const seen = new Set();
    function visit(value, depth) {
      const direct = normalizeTrackId(value);
      if (direct) return direct;
      if (!value || typeof value !== "object" || depth > 3 || seen.has(value)) return "";
      seen.add(value);
      for (const path of preferredPaths) {
        const nested = normalizeTrackId(getNestedValue(value, path));
        if (nested) return nested;
      }
      for (const key of Object.keys(value)) {
        if (/duration|progress|position|time|length/i.test(key)) continue;
        const nested = visit(value[key], depth + 1);
        if (nested) return nested;
      }
      return "";
    }
    for (const arg of args) {
      const id = visit(arg, 0);
      if (id) return id;
    }
    return "";
  }
  async function loadTrack(rawId) {
    const sequence = ++loadSequence;
    const trackId = String(rawId || "").split("_")[0];
    currentTrackId = trackId;
    currentTrack = null;
    if (!/^\d+$/.test(trackId)) {
      clear("invalid_track");
      return;
    }
    try {
      const [detail, lyric] = await Promise.all([
        fetchJson("https://music.163.com/api/song/detail?ids=[" + encodeURIComponent(trackId) + "]"),
        fetchJson("https://music.163.com/api/song/lyric/v1?tv=1&lv=1&rv=1&kv=1&yv=1&ytv=1&yrv=1&cp=false&id=" + encodeURIComponent(trackId))
      ]);
      if (sequence !== loadSequence || trackId !== currentTrackId) {
        return;
      }
      const song = detail && detail.songs && detail.songs[0] ? detail.songs[0] : {};
      const original = parseLrc(lyric && lyric.lrc ? lyric.lrc.lyric : "");
      const translation = parseLrc(lyric && lyric.tlyric ? lyric.tlyric.lyric : "");
      if (original.length === 0) {
        clear("no_lyrics");
        return;
      }
      currentTrack = {
        id: trackId,
        title: song.name || "",
        artist: Array.isArray(song.artists)
          ? song.artists.map(artist => artist.name).filter(Boolean).join(" / ")
          : "",
        original,
        translation
      };
      send({ type: "track", track: currentTrack });
      lastTrackSentAt = Date.now();
    } catch (error) {
      console.error("[Lyrics StatusBar] track load failed", error);
      if (sequence === loadSequence) {
        clear("load_failed");
      }
    }
  }

  function onProgress(rawSeconds) {
    if (!currentTrackId) {
      return;
    }
    const seconds = Number(rawSeconds);
    if (!Number.isFinite(seconds) || seconds < 0) {
      return;
    }
    if (seconds > 3600 && seconds % 1000 === 0) {
      sendProgress(Math.round(seconds));
      return;
    }
    sendProgress(Math.round(seconds * 1000));
  }

  function sendProgress(positionMs) {
    const now = Date.now();
    if (!currentTrack && currentTrackId && now - lastTrackRetryAt >= TRACK_RESEND_MS) {
      lastTrackRetryAt = now;
      loadTrack(currentTrackId);
    }
    if (currentTrack && now - lastTrackSentAt >= TRACK_RESEND_MS) {
      send({ type: "track", track: currentTrack });
      lastTrackSentAt = now;
    }
    if (now - lastProgressSentAt < 80) {
      return;
    }
    lastProgressSentAt = now;
    send({
      type: "progress",
      trackId: currentTrackId,
      positionMs
    });
  }
  function registerLegacyAudioplayerHooks() {
    if (!window.legacyNativeCmder || typeof legacyNativeCmder.appendRegisterCall !== "function") {
      return false;
    }
    legacyNativeCmder.appendRegisterCall("Load", "audioplayer", (...args) => {
      const trackId = extractTrackId(...args);
      if (trackId) {
        loadTrack(trackId);
      } else {
        console.warn("[Lyrics StatusBar] cannot extract track id from Load event", args);
        clear("missing_track_id");
      }
    });
    legacyNativeCmder.appendRegisterCall("PlayProgress", "audioplayer", (_, progress) => onProgress(progress));
    legacyNativeCmder.appendRegisterCall("PlayState", "audioplayer", (_, __, state) => {
      if (state === 2) {
        hide("paused");
      } else if (state === 1 && currentTrack) {
        send({ type: "track", track: currentTrack });
      }
    });
    return true;
  }

  function registerChannelHooks() {
    if (!window.channel || typeof channel.registerCall !== "function") {
      return false;
    }
    channel.registerCall("audioplayer.onLoad", (...args) => loadTrack(extractTrackId(...args)));
    channel.registerCall("audioplayer.onPlayProgress", (...args) => onProgress(args[1] ?? args[0]));
    return true;
  }

  function initialize() {
    try {
      const registered = registerLegacyAudioplayerHooks() || registerChannelHooks();
      send({
        type: "hello",
        pluginVersion: PLUGIN_VERSION,
        clientVersion: navigator.userAgent || "unknown"
      });
      setInterval(() => send({
        type: "hello",
        pluginVersion: PLUGIN_VERSION,
        clientVersion: navigator.userAgent || "unknown"
      }), 2000);
      if (!registered) {
        clear("no_audioplayer_hook");
        console.warn("[Lyrics StatusBar] no compatible audioplayer hook found");
      }
    } catch (error) {
      console.error("[Lyrics StatusBar] initialization failed", error);
      clear("initialization_failed");
    }
  }
  plugin.onLoad(initialize);
})();
