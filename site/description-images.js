// Shows the images of a listing description on a share page, on the static page and in the fallback alike.
// The site serves no description image, so the page fetches the bytes from the host of the author and applies
// the client rules of RFC 0058 that a browser can apply, which are https, the byte cap of the role, the format
// and the animation read from the bytes, and the digest, the byte count and the pixel size of the record.
// Only bytes that pass all of them reach the page, and they reach it as a blob of exactly those bytes.
// Anything else leaves the caption the page already shows.
// Two rules of the RFC stay with the browser, because fetch gives a page no way to apply them. The browser
// follows a redirect chain longer than three, and it alone knows the address a host name points at, so the
// page can only test that the address the bytes finally came from is still https. The digest bounds what
// either one can cost, because bytes that do not match the record never reach the page.
// The script runs before the body, so that the room of an image is on the page from the first paint and
// nothing moves when the bytes arrive.
(function () {
  "use strict";

  var CAP = 1024 * 1024;
  var PIXELS = 2048;
  // How near the viewport an image starts to load, because a description holds up to 16 of them.
  var MARGIN = "400px";
  // How long a host has to answer with the whole image, so that a stalled answer gives the caption back
  // instead of leaving an empty frame on the page.
  var DEADLINE = 15000;
  var SETTING = "borea.author-images";
  // The class that opens the room of every image on the page, which the style sheet reads.
  var SHOWING = "author-images";
  // The class that shows the switch, because a page that this script cannot drive must not offer one.
  var READY = "author-images-ready";
  var PNG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

  if (!window.fetch || !window.crypto || !crypto.subtle || !window.URL || !URL.createObjectURL) {
    return;
  }

  // One promise per digest, so an image a description shows twice is fetched once.
  var wanted = Object.create(null);

  function allowed() {
    try {
      return window.localStorage.getItem(SETTING) === "on";
    } catch (error) {
      return false;
    }
  }

  function remember(value) {
    try {
      window.localStorage.setItem(SETTING, value ? "on" : "off");
    } catch (error) {
      return;
    }
  }

  function showing(value) {
    document.documentElement.classList[value ? "add" : "remove"](SHOWING);
  }

  // An image that fails on its own bytes fails the same way every time, so the page keeps that answer and
  // asks the host again only when the host itself was the one that did not answer.
  function refused(message) {
    var error = new Error(message);
    error.permanent = true;
    return error;
  }

  function record(figure) {
    var url = figure.getAttribute("data-image");
    var digest = (figure.getAttribute("data-sha256") || "").toLowerCase();
    var width = Number(figure.getAttribute("data-width"));
    var height = Number(figure.getAttribute("data-height"));
    var size = Number(figure.getAttribute("data-size"));
    if (!/^[0-9a-f]{64}$/.test(digest) || !width || !height || !size) {
      return null;
    }
    if (width > PIXELS || height > PIXELS || size > CAP) {
      return null;
    }
    try {
      if (new URL(url, location.href).protocol !== "https:") {
        return null;
      }
    } catch (error) {
      return null;
    }
    return { url: url, sha256: digest, width: width, height: height, size: size };
  }

  function hex(buffer) {
    var digits = [];
    new Uint8Array(buffer).forEach(function (value) {
      digits.push((value < 16 ? "0" : "") + value.toString(16));
    });
    return digits.join("");
  }

  function tag(view, at) {
    return String.fromCharCode(view.getUint8(at), view.getUint8(at + 1), view.getUint8(at + 2), view.getUint8(at + 3));
  }

  function stillPng(view) {
    // An APNG names its animation control chunk before its first frame of image data, and the RFC forbids one.
    var at = 8;
    while (at + 12 <= view.byteLength) {
      var kind = tag(view, at + 4);
      if (kind === "acTL") {
        return false;
      }
      if (kind === "IDAT" || kind === "IEND") {
        return true;
      }
      at += 12 + view.getUint32(at, false);
    }
    return false;
  }

  function stillWebp(view) {
    var at = 12;
    while (at + 8 <= view.byteLength) {
      var kind = tag(view, at);
      var length = view.getUint32(at + 4, true);
      if (at + 8 + length > view.byteLength || kind === "ANIM" || kind === "ANMF") {
        return false;
      }
      if (kind === "VP8X" && length >= 10 && (view.getUint8(at + 8) & 0x02)) {
        return false;
      }
      at += 8 + length + (length % 2);
    }
    return true;
  }

  function mediaType(bytes) {
    // The format comes from the signature of the file, never from a header or from the address.
    var view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    if (bytes.length >= 8 && PNG.every(function (value, index) { return bytes[index] === value; })) {
      return stillPng(view) ? "image/png" : null;
    }
    if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
      return "image/jpeg";
    }
    if (bytes.length >= 12 && tag(view, 0) === "RIFF" && tag(view, 8) === "WEBP") {
      return stillWebp(view) ? "image/webp" : null;
    }
    return null;
  }

  function read(body, cap, stop) {
    var reader = body.getReader();
    var parts = [];
    var total = 0;
    return reader.read().then(function step(chunk) {
      if (chunk.done) {
        var bytes = new Uint8Array(total);
        var at = 0;
        parts.forEach(function (part) {
          bytes.set(part, at);
          at += part.length;
        });
        return bytes;
      }
      total += chunk.value.length;
      if (total > cap) {
        stop.abort();
        throw new Error("the image is larger than the cap of its role");
      }
      parts.push(chunk.value);
      return reader.read().then(step);
    });
  }

  function fetched(entry) {
    var stop = new AbortController();
    var deadline = window.setTimeout(function () { stop.abort(); }, DEADLINE);
    var kept = function (value) {
      window.clearTimeout(deadline);
      return value;
    };
    var lost = function (error) {
      window.clearTimeout(deadline);
      throw error;
    };
    return window.fetch(entry.url, {
      credentials: "omit",
      referrerPolicy: "no-referrer",
      mode: "cors",
      signal: stop.signal
    }).then(function (response) {
      if (!response.ok) {
        throw new Error("the host answered " + response.status);
      }
      // A redirect the browser followed may have left https, which the bytes of the record may not do.
      if (new URL(response.url).protocol !== "https:") {
        throw new Error("the image is not served over https");
      }
      var length = Number(response.headers.get("Content-Length"));
      if (length > entry.size) {
        throw new Error("the host offers more bytes than the record names");
      }
      return response.body ? read(response.body, entry.size, stop)
        : response.arrayBuffer().then(function (buffer) { return new Uint8Array(buffer); });
    }).then(function (bytes) {
      if (bytes.length > entry.size) {
        throw refused("the image has more bytes than its record names");
      }
      var type = mediaType(bytes);
      if (!type) {
        throw refused("the bytes are not a still PNG, JPEG or WebP");
      }
      return crypto.subtle.digest("SHA-256", bytes).then(function (digest) {
        if (hex(digest) !== entry.sha256) {
          throw refused("the image does not match the digest of its record");
        }
        // The byte count travels with the blob, because a second record can name the same digest and its
        // own byte count, and it must be held to the count it names.
        return { source: URL.createObjectURL(new Blob([bytes], { type: type })), size: bytes.length };
      });
    }).then(kept, lost);
  }

  function bytesOf(entry) {
    if (!wanted[entry.sha256]) {
      wanted[entry.sha256] = fetched(entry).catch(function (error) {
        // A host that was down once can answer the next time the reader asks for the image, but bytes that
        // do not match their record never will, so that answer stays.
        if (!error || !error.permanent) {
          delete wanted[entry.sha256];
        }
        throw error;
      });
    }
    return wanted[entry.sha256];
  }

  function decoded(image) {
    if (image.decode) {
      return image.decode();
    }
    return new Promise(function (resolve, reject) {
      image.onload = resolve;
      image.onerror = reject;
    });
  }

  function show(shot) {
    // One image at a time per figure, because a reader who turns the switch off and on again while the
    // bytes are on their way would otherwise put a second image in the same frame.
    if (shot.loading || shot.shown) {
      return Promise.resolve();
    }
    shot.loading = true;
    shot.figure.classList.remove("no-image");
    return bytesOf(shot.record).then(function (held) {
      if (held.size !== shot.record.size) {
        throw refused("the image has " + held.size + " bytes and the record names " + shot.record.size);
      }
      var image = new Image(shot.record.width, shot.record.height);
      image.alt = "";
      image.decoding = "async";
      image.src = held.source;
      return decoded(image).then(function () {
        if (image.naturalWidth !== shot.record.width || image.naturalHeight !== shot.record.height) {
          throw refused("the image does not have the size of its record");
        }
        shot.frame.appendChild(image);
        shot.shown = true;
        shot.loading = false;
      });
    }).catch(function () {
      // Nothing a reader has to read: the caption of the image stays, and the page works as before.
      shot.figure.classList.add("no-image");
      shot.failed = true;
      shot.loading = false;
    });
  }

  function watch(shots) {
    var waiting = shots.filter(function (shot) { return !shot.shown && !shot.failed; });
    if (!waiting.length) {
      return null;
    }
    if (!window.IntersectionObserver) {
      waiting.forEach(show);
      return null;
    }
    var byFigure = new Map();
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) {
          return;
        }
        observer.unobserve(entry.target);
        show(byFigure.get(entry.target));
      });
    }, { rootMargin: MARGIN });
    waiting.forEach(function (shot) {
      byFigure.set(shot.figure, shot);
      observer.observe(shot.figure);
    });
    return observer;
  }

  function switchBox(section) {
    // The page carries the switch, so that it stands in its place from the first paint. A page that was
    // written before the switch was part of it still gets one here.
    var line = section.querySelector(".image-switch");
    if (!line) {
      line = document.createElement("p");
      line.className = "image-switch";
      var label = document.createElement("label");
      var box = document.createElement("input");
      box.type = "checkbox";
      label.appendChild(box);
      label.appendChild(document.createTextNode(" Show images from author hosts"));
      line.appendChild(label);
      var note = document.createElement("span");
      note.className = "meta";
      note.textContent = "Every image comes from the host of its author, which then learns your address.";
      line.appendChild(note);
      section.insertBefore(line, section.querySelector(".prose"));
    }
    return line.querySelector("input[type=checkbox]");
  }

  function control(section, shots) {
    var box = switchBox(section);
    if (!box) {
      return;
    }
    box.checked = allowed();

    var observer = null;
    box.addEventListener("change", function () {
      remember(box.checked);
      if (observer) {
        observer.disconnect();
        observer = null;
      }
      showing(box.checked);
      if (box.checked) {
        // An image that failed is asked for again, because the reader has just asked for it again. An image
        // whose bytes did not match its record answers from the answer the page kept, with no new request.
        shots.forEach(function (shot) { shot.failed = false; });
        observer = watch(shots);
      }
    });
    if (box.checked) {
      observer = watch(shots);
    }
  }

  function start(root) {
    var section = root && root.querySelector ? (root.classList && root.classList.contains("description")
      ? root : root.querySelector(".description")) : null;
    if (!section || section.boreaImages) {
      return;
    }
    section.boreaImages = true;
    var shots = [];
    Array.prototype.forEach.call(section.querySelectorAll("figure[data-image]"), function (figure) {
      var entry = record(figure);
      var frame = figure.querySelector(".frame");
      if (entry && frame) {
        shots.push({ figure: figure, frame: frame, record: entry, shown: false, failed: false, loading: false });
      }
    });
    if (shots.length) {
      control(section, shots);
      return;
    }
    // A page with no image to fetch offers no switch, whatever it was written with.
    var idle = section.querySelector(".image-switch");
    if (idle && idle.parentNode) {
      idle.parentNode.removeChild(idle);
    }
  }

  window.boreaImages = { start: start };
  // Before the body is parsed, so that every figure has the room of its image in the first paint the reader
  // sees, and a figure gives the room back only when its bytes do not arrive or do not match their record.
  showing(allowed());
  document.documentElement.classList.add(READY);
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { start(document); });
  } else {
    start(document);
  }
})();
