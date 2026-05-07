(() => {
if (window.__sdmContentScriptLoaded) {
  return;
}
window.__sdmContentScriptLoaded = true;

const SDM_MEDIA_EXTENSIONS = [
  ".mp4", ".webm", ".mkv", ".mov", ".avi",
  ".mp3", ".m4a", ".wav", ".ogg",
  ".zip", ".rar", ".7z",
  ".pdf", ".exe", ".msi"
];

const SDM_VIDEO_HOSTS = [
  "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
  "facebook.com", "www.facebook.com", "m.facebook.com", "fb.watch",
  "instagram.com", "www.instagram.com",
  "twitter.com", "www.twitter.com", "x.com", "www.x.com",
  "tiktok.com", "www.tiktok.com",
  "vimeo.com", "www.vimeo.com",
  "dailymotion.com", "www.dailymotion.com",
  "twitch.tv", "www.twitch.tv",
  "reddit.com", "www.reddit.com",
  "rumble.com", "www.rumble.com",
  "odysee.com", "www.odysee.com",
];

let sdmButton = null;
let sdmDetectedUrl = "";
let sdmIsVideoSite = false;

scanForDownloadable();

// Re-scan when YouTube/Facebook navigate within the SPA without full page reload
let lastHref = location.href;
setInterval(() => {
  if (location.href !== lastHref) {
    lastHref = location.href;
    scanForDownloadable();
  }
}, 1000);

new MutationObserver(debounce(scanForDownloadable, 700)).observe(document.documentElement, {
  childList: true,
  subtree: true,
  attributes: true,
  attributeFilter: ["src", "href"]
});

function scanForDownloadable() {
  const hostname = location.hostname.toLowerCase();

  if (isVideoHost(hostname)) {
    if (isVideoPage(location.href)) {
      sdmDetectedUrl = location.href;
      sdmIsVideoSite = true;
      showButton("Download video with SDM");
    } else {
      removeButton();
    }
    return;
  }

  sdmIsVideoSite = false;
  const mediaUrl = findBestDirectMediaUrl();

  if (mediaUrl) {
    sdmDetectedUrl = mediaUrl;
    showButton("Download with SDM");
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
    /youtube\.com\/watch/i,
    /youtu\.be\/.+/i,
    /youtube\.com\/shorts\/.+/i,
    /facebook\.com\/reel\//i,
    /facebook\.com\/watch/i,
    /facebook\.com\/.*\/videos\//i,
    /facebook\.com\/.*\/posts\//i,
    /facebook\.com\/share\/[rv]\//i,
    /facebook\.com\/\w+\/videos/i,
    /fb\.watch\/.+/i,
    /instagram\.com\/(?:p|reel|tv)\//i,
    /(?:twitter|x)\.com\/.+\/status\//i,
    /tiktok\.com\/@[^/]+\/video\//i,
    /vimeo\.com\/\d+/i,
    /dailymotion\.com\/video\//i,
    /twitch\.tv\/(?:videos\/\d+|[^/]+\/clip\/)/i,
    /reddit\.com\/r\/[^/]+\/comments\//i,
    /rumble\.com\/v/i,
    /odysee\.com\/@[^/]+\//i,
  ];
  return patterns.some((p) => p.test(url));
}

function findBestDirectMediaUrl() {
  const candidates = [];
  document.querySelectorAll("video[src], audio[src], source[src]").forEach((el) => {
    candidates.push(el.currentSrc || el.src);
  });
  document.querySelectorAll("a[href]").forEach((a) => {
    candidates.push(a.href);
  });
  return candidates.map(normalizeUrl).filter(Boolean).find(isDirectDownloadUrl) || "";
}

function normalizeUrl(url) {
  try { return new URL(url, location.href).href; } catch { return ""; }
}

function isDirectDownloadUrl(url) {
  try {
    const parsed = new URL(url);
    if (!["http:", "https:"].includes(parsed.protocol)) return false;
    const pathname = parsed.pathname.toLowerCase();
    return SDM_MEDIA_EXTENSIONS.some((ext) => pathname.endsWith(ext));
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
      "height: 40px",
      "padding: 0 16px",
      "border: 0",
      "border-radius: 6px",
      "background: #1f6feb",
      "color: white",
      "font: 600 13px Segoe UI, Arial, sans-serif",
      "box-shadow: 0 8px 24px rgba(16,35,63,.28)",
      "cursor: pointer",
      "transition: background 0.2s",
      "white-space: nowrap",
    ].join(";");
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
  setButtonState("Sending\u2026", "#475467");

  // IMPORTANT: For video sites, always send location.href (the page URL).
  // Never send CDN/stream URLs — they use short-lived auth tokens and return 400.
  // yt-dlp on the desktop will extract the real video stream from the page URL.
  const urlToSend = sdmIsVideoSite ? location.href : sdmDetectedUrl;
  const referrer = sdmIsVideoSite ? location.href : "";

  if (!canSendRuntimeMessage()) {
    setButtonState("Reload page & retry", "#d92d20");
    resetButtonSoon();
    return;
  }

  // FIX: Added explicit error handling for cases where the extension
  // context is invalidated (e.g. extension reloaded while page is open)
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
          setButtonState("Sent \u2713", "#12805c");
        } else {
          setButtonState(response?.message || "Bridge not ready", "#d92d20");
        }
        resetButtonSoon();
      }
    );
  } catch (err) {
    // Extension context invalidated — page needs refresh
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
    sdmButton.textContent = sdmIsVideoSite ? "Download video with SDM" : "Download with SDM";
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
