// Marks a phone or a tablet before the body is parsed, because nothing there opens a borea:// link.
(function () {
  "use strict";

  var HANDHELD = "handheld";
  var DEVICE = /Android|iPhone|iPad|iPod|Mobi/;

  // Client hints add to the user agent and never overrule it, because Chrome on an Android tablet reports
  // mobile as false. An iPad asks for the desktop site and then names itself a Mac, but a Mac has no touch screen.
  function isHandheld(nav) {
    var hints = nav.userAgentData;
    if (hints && (hints.mobile === true || hints.platform === "Android")) {
      return true;
    }
    var agent = String(nav.userAgent || "");
    return DEVICE.test(agent) || (/Macintosh/.test(agent) && nav.maxTouchPoints > 1);
  }

  // The tests in .github/scripts/tests load this file in Node.
  if (typeof document === "undefined") {
    module.exports = { isHandheld: isHandheld };
    return;
  }

  if (isHandheld(navigator)) {
    document.documentElement.classList.add(HANDHELD);
  }
})();
