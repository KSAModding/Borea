(function () {
  "use strict";

  var API = "https://api.github.com/repos/KSAModding/Borea/releases/latest";
  var APP_ARCHIVE = /^Borea-[^-]+.*-(win-x64\.zip|linux-x64\.tar\.gz|macos-arm64\.tar\.gz)$/;
  var TARGETS = {
    windows: { label: "Download for Windows", suffix: "-win-x64.zip" },
    linux: { label: "Download for Linux", suffix: "-linux-x64.tar.gz" },
    macos: { label: "Download for macOS", suffix: "-macos-arm64.tar.gz" }
  };

  function detectOs(nav) {
    var data = nav.userAgentData;
    var text = (data && data.platform) || nav.userAgent || "";
    if ((data && data.mobile) || /Android|iPhone|iPad|iPod|iOS/i.test(text)) {
      return "mobile";
    }
    if (/Mac/i.test(text)) {
      // iPadOS reports itself as a Mac, but a Mac has no touch screen.
      return nav.maxTouchPoints > 1 ? "mobile" : "macos";
    }
    if (/Win/i.test(text)) {
      return "windows";
    }
    if (/Linux|X11/i.test(text) && !/CrOS/.test(text)) {
      return "linux";
    }
    return "unknown";
  }

  function findArchive(assets, suffix) {
    for (var i = 0; i < (assets || []).length; i++) {
      var name = assets[i].name || "";
      if (APP_ARCHIVE.test(name) && name.indexOf("Borea-Cli-") !== 0 && name.slice(-suffix.length) === suffix) {
        return assets[i].browser_download_url;
      }
    }
    return null;
  }

  var os = detectOs(navigator);
  var button = document.getElementById("download");
  var notes = document.getElementById("notes");

  if (os === "mobile") {
    button.hidden = true;
    document.getElementById("available").hidden = false;
  }

  fetch(API, { headers: { Accept: "application/vnd.github+json" } })
    .then(function (response) {
      return response.ok ? response.json() : null;
    })
    .then(function (release) {
      if (!release || typeof release.tag_name !== "string") {
        return;
      }
      notes.textContent = release.tag_name + " release notes";
      if (release.html_url) {
        notes.href = release.html_url;
      }
      var target = TARGETS[os];
      var url = target && findArchive(release.assets, target.suffix);
      if (url) {
        button.href = url;
        document.getElementById("download-label").textContent = target.label;
      }
    })
    .catch(function () {});
})();
