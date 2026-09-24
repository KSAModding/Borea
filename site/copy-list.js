// Copies the forum list of a pack share page, and the address of a share page for a reader on a phone,
// on the static page and in the fallback alike.
// A button stays hidden where the browser gives a page no clipboard, and the list stays on the page to select by hand.
(function () {
  "use strict";

  // How long a button says what happened before it reads its own label again.
  var FEEDBACK = 2000;

  if (!navigator.clipboard || !navigator.clipboard.writeText) {
    return;
  }

  // The canonical address of a static page, so a copied link carries no query or fragment of this visit.
  function address() {
    var canonical = document.querySelector('link[rel="canonical"]');
    return canonical ? canonical.href : location.href;
  }

  function wire(button, value, failure) {
    if (button.boreaCopyList) {
      return;
    }
    button.boreaCopyList = true;
    var label = button.textContent;
    var timer = null;
    function say(text) {
      button.textContent = text;
      clearTimeout(timer);
      timer = setTimeout(function () { button.textContent = label; }, FEEDBACK);
    }
    button.addEventListener("click", function () {
      navigator.clipboard.writeText(value()).then(
        function () { say("Copied"); },
        function () { say(failure); });
    });
    button.hidden = false;
  }

  function start(root) {
    Array.prototype.forEach.call(root.querySelectorAll("button[data-copy-list]"), function (button) {
      var list = button.parentNode.querySelector(".forum-list");
      if (list) {
        wire(button, function () { return list.textContent; }, "Could not copy, select the list instead");
      }
    });
    Array.prototype.forEach.call(root.querySelectorAll("button[data-copy-link]"), function (button) {
      wire(button, address, "Could not copy, use the address bar instead");
    });
  }

  window.boreaCopyList = { start: start };
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { start(document); });
  } else {
    start(document);
  }
})();
