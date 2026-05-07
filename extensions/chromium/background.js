const HOST_NAME = "com.sis.sdm";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "download-link-with-sdm",
    title: "Download with Silent Download Manager",
    contexts: ["link"]
  });

  chrome.contextMenus.create({
    id: "download-page-with-sdm",
    title: "Download current page with Silent Download Manager",
    contexts: ["page"]
  });

  chrome.contextMenus.create({
    id: "download-video-with-sdm",
    title: "Download video with Silent Download Manager",
    contexts: ["video"]
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "download-video-with-sdm") {
    const pageUrl = info.pageUrl || tab?.url || "";
    const srcUrl = info.srcUrl || "";
    const isVideoSite = isKnownVideoSite(pageUrl);
    const urlToSend = isVideoSite ? pageUrl : (srcUrl || pageUrl);
    sendToNativeHost(urlToSend, pageUrl);
    return;
  }

  const url = info.linkUrl || info.pageUrl || tab?.url;
  const referrer = info.pageUrl || tab?.url || "";

  if (!url) {
    notify("No downloadable URL found.");
    return;
  }

  sendToNativeHost(url, referrer);
});

// FIX: Proper async message handling with .catch() to prevent uncaught promise errors
// and ensure sendResponse is always called even when native host is unavailable.
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "sdm-download-url" && message.url) {
    sendToNativeHost(message.url, message.referrer || sender.tab?.url || "")
      .then((response) => sendResponse(response))
      .catch((err) => {
        console.error("[SDM] sendToNativeHost failed:", err);
        sendResponse({ ok: false, message: "Native host error. Is SDM running?" });
      });
    return true; // Keep the message channel open for async response
  }
  return false;
});

function isKnownVideoSite(url) {
  try {
    const host = new URL(url).hostname.toLowerCase().replace(/^(www\.|m\.)/, "");
    const sites = [
      "youtube.com", "youtu.be",
      "facebook.com", "fb.watch",
      "instagram.com",
      "twitter.com", "x.com",
      "tiktok.com", "vimeo.com",
      "dailymotion.com", "twitch.tv",
      "reddit.com", "rumble.com", "odysee.com",
    ];
    return sites.some((s) => host === s || host.endsWith("." + s));
  } catch {
    return false;
  }
}

async function sendToNativeHost(url, referrer = "") {
  try {
    const response = await chrome.runtime.sendNativeMessage(HOST_NAME, { url, referrer });
    const message = response?.message || "Sent to Silent Download Manager.";
    notify(message);
    return { ok: response?.ok !== false, message };
  } catch (error) {
    // FIX: More descriptive error message to help users understand what went wrong
    const message = "SDM native host not reachable. Make sure the desktop app is installed and the native host is registered.";
    notify(message);
    return { ok: false, message };
  }
}

function notify(message) {
  chrome.notifications.create({
    type: "basic",
    iconUrl: "icon-128.png",
    title: "Silent Download Manager",
    message
  });
}
