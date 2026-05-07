(() => {
if (window.__sdmContentScriptLoaded) {
  return;
}
window.__sdmContentScriptLoaded = true;

// Expanded file extensions - iso, img, dmg, apk, and many more
const SDM_MEDIA_EXTENSIONS = [
  // Video
  ".mp4", ".webm", ".mkv", ".mov", ".avi", ".flv", ".wmv", ".m4v",
  ".3gp", ".3g2", ".ts", ".mts", ".m2ts", ".vob", ".ogv", ".rm", ".rmvb",
  // Audio
  ".mp3", ".m4a", ".wav", ".ogg", ".flac", ".aac", ".wma", ".opus",
  ".aiff", ".ape", ".mka",
  // Archives
  ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".zst",
  ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz",
  // Disk images
  ".iso", ".img", ".dmg", ".bin", ".cue", ".nrg", ".mdf",
  // Documents
  ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
  ".odt", ".ods", ".odp", ".epub", ".mobi",
  // Executables / installers
  ".exe", ".msi", ".msix", ".appx", ".deb", ".rpm", ".pkg",
  ".apk", ".ipa", ".crx", ".xpi",
  // Data / dev
  ".torrent", ".json", ".xml", ".csv", ".sql", ".db", ".sqlite",
  // Images (large/raw)
  ".psd", ".ai", ".raw", ".cr2", ".nef", ".arw", ".dng",
];

const SDM_VIDEO_HOSTS = [
  "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
  "music.youtube.com",
  "facebook.com", "www.facebook.com", "m.facebook.com", "fb.watch", "fb.com",
  "instagram.com", "www.instagram.com", "m.instagram.com",
  "twitter.com", "www.twitter.com", "x.com", "www.x.com",
  "tiktok.com", "www.tiktok.com", "m.tiktok.com",
  "vimeo.com", "www.vimeo.com", "player.vimeo.com",
  "dailymotion.com", "www.dailymotion.com",
  "twitch.tv", "www.twitch.tv", "clips.twitch.tv",
  "reddit.com", "www.reddit.com", "old.reddit.com",
  "rumble.com", "www.rumble.com",
  "odysee.com", "www.odysee.com",
  "bilibili.com", "www.bilibili.com",
  "nicovideo.jp", "www.nicovideo.jp",
  "streamable.com", "www.streamable.com",
  "9gag.com", "www.9gag.com",
  "imgur.com", "www.imgur.com", "i.imgur.com",
  "gfycat.com", "www.gfycat.com",
  "pinterest.com", "www.pinterest.com",
  "linkedin.com", "www.linkedin.com",
  "snapchat.com", "www.snapchat.com",
];

let sdmButton = null;
let sdmDetectedUrl = "";
let sdmIsVideoSite = false;

scanForDownloadable();

// Re-scan when SPA navigates without full page reload
let lastHref = location.href;
setInterval(() => {
  if (location.href !== lastHref) {
    lastHref = location.href;
    // Small delay so page content loads
    setTimeout(scanForDownloadable, 800);
  }
}, 800);

// Also scan after DOM mutations (catches lazy-loaded content)
new MutationObserver(debounce(scanForDownloadable, 900)).observe(document.documentElement, {
  childList: true,
  subtree: true,
  attributes: true,
  attributeFilter: ["src", "href", "data-src", "data-url"]
});

function scanForDownloadable() {
  const hostname = location.hostname.toLowerCase();

  if (isVideoHost(hostname)) {
    if (isVideoPage(location.href)) {
      sdmDetectedUrl = location.href;
      sdmIsVideoSite = true;
      showButton("⬇ Download video with SDM");
    } else {
      removeButton();
    }
    return;
  }

  sdmIsVideoSite = false;
  const mediaUrl = findBestDirectMediaUrl();

  if (mediaUrl) {
    sdmDetectedUrl = mediaUrl;
    showButton("⬇ Download with SDM");
  } else {
    removeButton();
  }
}

function isVideoHost(hostname) {
  return SDM_VIDEO_HOSTS.some(
    (h) => hostname === h || hostname.endsWith("." + h)
  );
}

function isVideoPage(url) {
  const patterns = [
    // YouTube
    /youtube\.com\/watch/i,
    /youtu\.be\/.+/i,
    /youtube\.com\/shorts\/.+/i,
    /youtube\.com\/live\/.+/i,
    /music\.youtube\.com\/watch/i,
    // Facebook - expanded patterns
    /facebook\.com\/reel\//i,
    /facebook\.com\/watch/i,
    /facebook\.com\/.*\/videos\//i,
    /facebook\.com\/.*\/posts\//i,
    /facebook\.com\/share\/[rv]\//i,
    /facebook\.com\/\w+\/videos/i,
    /facebook\.com\/video\//i,
    /facebook\.com\/permalink/i,
    /fb\.watch\/.+/i,
    /fb\.com\/.+/i,
    // Instagram - expanded
    /instagram\.com\/(?:p|reel|tv)\//i,
    /instagram\.com\/stories\//i,
    /instagram\.com\/.*\/p\//i,
    /instagram\.com\/.*\/reel\//i,
    // Twitter/X
    /(?:twitter|x)\.com\/.+\/status\//i,
    // TikTok
    /tiktok\.com\/@[^/]+\/video\//i,
    /tiktok\.com\/t\//i,
    // Vimeo
    /vimeo\.com\/\d+/i,
    /vimeo\.com\/channels\/.+\/\d+/i,
    // Dailymotion
    /dailymotion\.com\/video\//i,
    // Twitch
    /twitch\.tv\/(?:videos\/\d+|[^/]+\/clip\/)/i,
    /clips\.twitch\.tv\//i,
    // Reddit
    /reddit\.com\/r\/[^/]+\/comments\//i,
    // Others
    /rumble\.com\/v/i,
    /odysee\.com\/@[^/]+\//i,
    /bilibili\.com\/video\//i,
    /nicovideo\.jp\/watch\//i,
    /streamable\.com\//i,
    /9gag\.com\/gag\//i,
    /gfycat\.com\//i,
    /pinterest\.com\/pin\//i,
    /linkedin\.com\/posts\//i,
    /linkedin\.com\/feed\/update\//i,
    /snapchat\.com\/add\//i,
  ];
  return patterns.some((p) => p.test(url));
}

function findBestDirectMediaUrl() {
  const candidates = new Set();

  // 1. <video> and <audio> source elements
  document.querySelectorAll("video, audio").forEach((el) => {
    if (el.src) candidates.add(el.src);
    if (el.currentSrc) candidates.add(el.currentSrc);
    el.querySelectorAll("source[src]").forEach((s) => {
      if (s.src) candidates.add(s.src);
    });
  });

  // 2. <source> elements anywhere
  document.querySelectorAll("source[src]").forEach((el) => {
    candidates.add(el.src);
  });

  // 3. Direct anchor links
  document.querySelectorAll("a[href]").forEach((a) => {
    candidates.add(a.href);
  });

  // 4. data-src / data-url attributes (lazy-loaded content)
  document.querySelectorAll("[data-src], [data-url], [data-video-src]").forEach((el) => {
    const v = el.getAttribute("data-src") || el.getAttribute("data-url") || el.getAttribute("data-video-src");
    if (v) candidates.add(v);
  });

  // 5. Object/embed elements
  document.querySelectorAll("object[data], embed[src]").forEach((el) => {
    const v = el.getAttribute("data") || el.getAttribute("src");
    if (v) candidates.add(v);
  });

  // Find best match - prefer video/audio over other files
  const normalized = [...candidates].map(normalizeUrl).filter(Boolean);

  // First pass: prefer video/audio extensions
  const videoExts = [".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".flv", ".mp3", ".m4a", ".flac", ".wav"];
  const videoMatch = normalized.find((u) => isDirectDownloadUrl(u) && videoExts.some(e => u.toLowerCase().includes(e)));
  if (videoMatch) return videoMatch;

  // Second pass: any downloadable file
  return normalized.find(isDirectDownloadUrl) || "";
}

function normalizeUrl(url) {
  try { return new URL(url, location.href).href; } catch { return ""; }
}

function isDirectDownloadUrl(url) {
  try {
    const parsed = new URL(url);
    if (!["http:", "https:"].includes(parsed.protocol)) return false;
    const pathname = parsed.pathname.toLowerCase();
    // Strip query params for extension check
    return SDM_MEDIA_EXTENSIONS.some((ext) => {
      // Check pathname ends with ext, or has ext before query/hash
      return pathname.endsWith(ext) || pathname.includes(ext + "?") || pathname.includes(ext + "&");
    });
  } catch { return false; }
}

function showButton(label) {
  if (!sdmButton) {
    sdmButton = document.createElement("button");
    sdmButton.type = "button";
    sdmButton.style.cssText = [
      "position: fixed",
      "right: 18px",
      "bottom: 18px",
      "z-index: 2147483647",
      "height: 42px",
      "padding: 0 18px",
      "border: 0",
      "border-radius: 8px",
      "background: #1f6feb",
      "color: white",
      "font: 600 13px Segoe UI, Arial, sans-serif",
      "box-shadow: 0 8px 24px rgba(16,35,63,.35)",
      "cursor: pointer",
      "transition: background 0.2s, transform 0.1s",
      "white-space: nowrap",
      "letter-spacing: 0.01em",
    ].join(";");
    sdmButton.addEventListener("mouseenter", () => { if (sdmButton) sdmButton.style.background = "#1a5fcc"; });
    sdmButton.addEventListener("mouseleave", () => { if (sdmButton) sdmButton.style.background = "#1f6feb"; });
    sdmButton.addEventListener("click", onButtonClick);
    document.documentElement.appendChild(sdmButton);
  }
  sdmButton.textContent = label;
  sdmButton.title = sdmIsVideoSite
    ? "Send this video page to Silent Download Manager (uses yt-dlp)"
    : "Send detected file to Silent Download Manager";
}

function onButtonClick() {
  if (!sdmDetectedUrl) return;
  setButtonState("Sending…", "#475467");

  const urlToSend = sdmIsVideoSite ? location.href : sdmDetectedUrl;
  const referrer = sdmIsVideoSite ? location.href : "";

  if (!canSendRuntimeMessage()) {
    setButtonState("Reload page & retry", "#d92d20");
    resetButtonSoon();
    return;
  }

  try {
    chrome.runtime.sendMessage(
      { type: "sdm-download-url", url: urlToSend, referrer },
      (response) => {
        const runtimeError = chrome.runtime?.lastError;
        if (runtimeError) {
          setButtonState(getRuntimeErrorLabel(runtimeError.message), "#d92d20");
          resetButtonSoon();
          return;
        }
        if (response?.ok) {
          setButtonState("Sent ✓", "#12805c");
        } else {
          setButtonState(response?.message || "Bridge not ready", "#d92d20");
        }
        resetButtonSoon();
      }
    );
  } catch (err) {
    setButtonState("Reload page & retry", "#d92d20");
    resetButtonSoon();
  }
}

function canSendRuntimeMessage() {
  return typeof chrome !== "undefined" &&
    chrome.runtime &&
    chrome.runtime.id &&
    typeof chrome.runtime.sendMessage === "function";
}

function getRuntimeErrorLabel(message = "") {
  return message.toLowerCase().includes("context invalidated")
    ? "Reload page & retry"
    : "Extension error";
}

function setButtonState(text, color) {
  if (!sdmButton) return;
  sdmButton.textContent = text;
  sdmButton.style.background = color;
}

function resetButtonSoon() {
  setTimeout(() => {
    if (!sdmButton) return;
    sdmButton.textContent = sdmIsVideoSite ? "⬇ Download video with SDM" : "⬇ Download with SDM";
    sdmButton.style.background = "#1f6feb";
  }, 2400);
}

function removeButton() {
  sdmDetectedUrl = "";
  sdmIsVideoSite = false;
  if (sdmButton) {
    sdmButton.remove();
    sdmButton = null;
  }
}

function debounce(fn, wait) {
  let t;
  return () => { clearTimeout(t); t = setTimeout(fn, wait); };
}
})();
