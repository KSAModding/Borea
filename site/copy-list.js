// Copies the forum list of a pack share page, on the static page and in the fallback alike.
// The button stays hidden where the browser gives a page no clipboard, and the list stays on the page to select by hand.
(function () {
  "use strict";

  // How long the button says what happened before it reads "Copy forum list" again.
  var FEEDBACK = 2000;

  if (!navigator.clipboard || !navigator.clipboard.writeText) {
    return;
  }

  function start(root) {
    Array.prototype.forEach.call(root.querySelectorAll("button[data-copy-list]"), function (button) {
      var list = button.parentNode.querySelector(".forum-list");
      if (!list || button.boreaCopyList) {
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
        navigator.clipboard.writeText(list.textContent).then(
          function () { say("Copied"); },
          function () { say("Could not copy, select the list instead"); });
      });
      button.hidden = false;
    });
  }

  window.boreaCopyList = { start: start };
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { start(document); });
  } else {
    start(document);
  }
})();
