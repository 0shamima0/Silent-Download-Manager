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

  chrome.contextMenus.create({
    id: "download-audio-with-sdm",
    title: "Download audio with Silent Download Manager",
    contexts: ["audio"]
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "download-video-with-sdm" || info.menuItemId === "download-audio-with-sdm") {
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

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "sdm-download-url" && message.url) {
    sendToNativeHost(message.url, message.referrer || sender.tab?.url || "")
      .then((response) => sendResponse(response))
      .catch((err) => {
        console.error("[SDM] sendToNativeHost failed:", err);
        sendResponse({ ok: false, message: "Native host error. Is SDM running?" });
      });
    return true;
  }

  // Quality selection request from popup
  if (message?.type === "sdm-download-with-quality" && message.url && message.quality) {
    sendToNativeHost(message.url, message.referrer || "", message.quality)
      .then((response) => sendResponse(response))
      .catch((err) => {
        sendResponse({ ok: false, message: "Native host error." });
      });
    return true;
  }

  return false;
});

function isKnownVideoSite(url) {
  try {
    const host = new URL(url).hostname.toLowerCase().replace(/^(www\.|m\.)/, "");
    const sites = [
      "youtube.com", "youtu.be", "music.youtube.com",
      "facebook.com", "fb.watch", "fb.com",
      "instagram.com",
      "twitter.com", "x.com",
      "tiktok.com",
      "vimeo.com",
      "dailymotion.com",
      "twitch.tv",
      "reddit.com",
      "rumble.com",
      "odysee.com",
      "bilibili.com",
      "nicovideo.jp",
      "streamable.com",
      "9gag.com",
      "imgur.com",
      "gfycat.com",
      "pinterest.com",
      "linkedin.com",
      "snapchat.com",
    ];
    return sites.some((s) => host === s || host.endsWith("." + s));
  } catch {
    return false;
  }
}

async function sendToNativeHost(url, referrer = "", quality = "") {
  try {
    const payload = { url, referrer };
    if (quality) payload.quality = quality;

    const response = await chrome.runtime.sendNativeMessage(HOST_NAME, payload);
    const message = response?.message || "Sent to Silent Download Manager.";
    notify(message);
    return { ok: response?.ok !== false, message };
  } catch (error) {
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
