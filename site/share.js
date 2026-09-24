// Renders the share page of a listing that is newer than the last site build, from the snapshot.
// Authors write the snapshot, so its values reach the page only through textContent and checked URLs.
(function () {
  "use strict";

  var SNAPSHOT = "https://ksamodding.github.io/content-index-releases/v1/index.json";
  var ID = /^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/;
  var RESERVED = /^(core|con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;
  var CONTROL = /[\x00-\x1f\x7f]/g;
  // The same, but a description keeps its line breaks and its tabs.
  var BLOCK_CONTROL = /[\x00-\x08\x0b-\x1f\x7f]/g;
  var DOT = String.fromCharCode(0xb7);
  // No prototype, so an author key such as "constructor" finds nothing.
  var TYPES = Object.assign(Object.create(null), { mod: "Mod", "mod-loader": "Mod loader", modpack: "Modpack" });
  var LINKS = Object.assign(Object.create(null), { forums: "Forum", repository: "Repository", spacedock: "SpaceDock", bugtracker: "Bug tracker", homepage: "Homepage", discussions: "Discussions" });
  var LINK_ORDER = Object.keys(LINKS);
  // ReleaseChannels in Borea.Core: stable offers stable releases, testing adds testing ones, and dev offers every status.
  var CHANNELS = Object.assign(Object.create(null), { stable: 0, testing: 1 });
  var DEV_CHANNEL = 2;
  var MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
  // The blocks and the inline spans the share page generator reads, kept the same by the parity test.
  var HEADING = /^(#{1,6})\s+(.*)$/;
  var ITEM = /^\s*([-*+]|\d+\.)\s+(.*)$/;
  var IMAGE = "!\\[(?<alt>[^\\]]*)\\]\\((?<target>(?:[^\\s()]|\\([^\\s()]*\\))*)(?:\\s+[^)]*)?\\)";
  // The same image, as the whole label of a link, so a linked image becomes the image and keeps its link.
  var IMAGE_ONLY = new RegExp("^(?:" + IMAGE + ")$", "u");
  // An image inside the label of a link, without the capture groups an image of its own has.
  var NESTED_IMAGE = "!\\[[^\\]]*\\]\\((?:[^\\s()]|\\([^\\s()]*\\))*(?:\\s+[^)]*)?\\)";
  var INLINE = new RegExp([
    IMAGE,
    "`(?<code>[^`]+)`",
    "\\[(?<label>(?:" + NESTED_IMAGE + "|[^\\]])*)\\]\\((?<url>(?:[^\\s()]|\\([^\\s()]*\\))*)(?:\\s+[^)]*)?\\)",
    "\\*\\*(?<strong>[^*]+)\\*\\*",
    "(?<![\\p{L}\\p{N}_])__(?<strongScore>[^_]+)__(?![\\p{L}\\p{N}_])",
    "\\*(?<emphasis>[^*]+)\\*",
    "(?<![\\p{L}\\p{N}_])_(?<emphasisScore>[^_]+)_(?![\\p{L}\\p{N}_])"
  ].join("|"), "gu");
  var FENCE = "```";
  // A heading of a description sits below the name of the listing and the heading of its section.
  var HEADING_OFFSET = 2;
  // Emphasis inside emphasis stops here, so a pathological description cannot recurse without an end.
  var SPAN_DEPTH = 3;

  // RFC 0058: a description shows the images of its own records, which it names as ksa-image:<id>, and no other image.
  var IMAGE_REFERENCE = "ksa-image:";
  var IMAGE_ID = /^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,62}[A-Za-z0-9])?$/;
  var SHA256 = /^[0-9a-f]{64}$/;
  var IMAGE_PIXELS = 2048;
  var IMAGE_CAP = 1024 * 1024;
  // What an image with no words of its own is called, so that a reader always sees a line where an image is.
  var IMAGE_PLACEHOLDER = "Image";

  function isObject(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value);
  }

  function validId(value) {
    return typeof value === "string" && ID.test(value) && !RESERVED.test(value.split(".")[0]);
  }

  function text(value) {
    if (typeof value !== "string") {
      return null;
    }
    var collapsed = value.replace(CONTROL, " ").split(/\s+/).filter(Boolean).join(" ");
    return collapsed || null;
  }

  function richText(value) {
    if (typeof value !== "string") {
      return null;
    }
    var cleaned = value.replace(/\r\n?/g, "\n").replace(BLOCK_CONTROL, " ").trim();
    return cleaned || null;
  }

  function webUrl(value) {
    if (typeof value !== "string" || /\s/.test(value) || value.replace(CONTROL, "") !== value) {
      return null;
    }
    try {
      var url = new URL(value);
      return (url.protocol === "http:" || url.protocol === "https:") && url.hostname ? url : null;
    } catch (error) {
      return null;
    }
  }

  function httpsUrl(value) {
    var url = webUrl(value);
    return url && url.protocol === "https:" ? value : null;
  }

  function count(value) {
    return typeof value === "number" && Number.isInteger(value) && value >= 0 ? value : null;
  }

  function stateOf(entry) {
    return isObject(entry.index_status) && typeof entry.index_status.state === "string" ? entry.index_status.state : null;
  }

  function dateText(value) {
    if (typeof value !== "string") {
      return null;
    }
    var date = new Date(value);
    return isNaN(date) ? null : date.getUTCDate() + " " + MONTHS[date.getUTCMonth()] + " " + date.getUTCFullYear();
  }

  function gameText(minimum, maximum) {
    if (!minimum) {
      return null;
    }
    if (!maximum) {
      return minimum + " or newer";
    }
    return minimum === maximum ? minimum : minimum + " to " + maximum;
  }

  function gameVersion(versions, revision, fallback) {
    revision = count(revision);
    for (var i = 0; revision !== null && i < versions.length; i++) {
      if (typeof versions[i] === "string" && versions[i].split(".").pop() === String(revision)) {
        return versions[i];
      }
    }
    return text(fallback);
  }

  function linksOf(authored) {
    var links = [];
    if (!isObject(authored.links)) {
      return links;
    }
    Object.keys(authored.links).forEach(function (key) {
      var url = webUrl(authored.links[key]);
      var label = text(key);
      if (url && label) {
        var known = key.toLowerCase();
        var rank = LINK_ORDER.indexOf(known);
        links.push({ rank: rank < 0 ? LINK_ORDER.length : rank, label: LINKS[known] || label.charAt(0).toUpperCase() + label.slice(1), url: url });
      }
    });
    return links.sort(function (a, b) { return a.rank - b.rank; });
  }

  function authorsOf(authored) {
    return Array.isArray(authored.authors) ? authored.authors.map(text).filter(Boolean) : [];
  }

  function displayTags(snapshot, tags) {
    var written = Array.isArray(tags) ? tags.map(text).filter(Boolean) : [];
    var chosen = written.map(function (name) { return name.toLowerCase(); });
    var curated = isObject(snapshot) && isObject(snapshot.tags) && Array.isArray(snapshot.tags.mod) ? snapshot.tags.mod : [];
    var known = [];
    var names = [];
    curated.forEach(function (tag) {
      var key = isObject(tag) ? text(tag.tag) : null;
      var name = isObject(tag) ? text(tag.name) : null;
      if (!key || !name) {
        return;
      }
      known.push(key.toLowerCase());
      if (chosen.indexOf(key.toLowerCase()) >= 0) {
        names.push(name);
      }
    });
    return names.concat(written.filter(function (name) { return known.indexOf(name.toLowerCase()) < 0; }));
  }

  function imageReference(target) {
    if (typeof target !== "string" || target.indexOf(IMAGE_REFERENCE) !== 0) {
      return null;
    }
    var name = target.slice(IMAGE_REFERENCE.length);
    return IMAGE_ID.test(name) ? name : null;
  }

  function imageRecord(record) {
    if (!isObject(record)) {
      return null;
    }
    var url = httpsUrl(record.url);
    var digest = typeof record.sha256 === "string" ? record.sha256.toLowerCase() : "";
    var width = count(record.width);
    var height = count(record.height);
    var size = count(record.size);
    if (typeof record.id !== "string" || !IMAGE_ID.test(record.id) || !url || !SHA256.test(digest)) {
      return null;
    }
    if (!width || width > IMAGE_PIXELS || !height || height > IMAGE_PIXELS || !size || size > IMAGE_CAP) {
      return null;
    }
    return {
      id: record.id, url: url, sha256: digest, width: width, height: height, size: size,
      attribution: text(record.attribution), source: httpsUrl(record.source)
    };
  }

  function imageRecords(authored) {
    var images = isObject(authored) && isObject(authored.images) ? authored.images : null;
    var records = images && Array.isArray(images.description) ? images.description : [];
    // No prototype, so a record id such as "constructor" finds nothing of its own.
    var found = Object.create(null);
    records.forEach(function (record) {
      var checked = imageRecord(record);
      if (checked && found[checked.id] === undefined) {
        found[checked.id] = checked;
      }
    });
    return found;
  }

  function imageSpans(alt, target, href) {
    var reference = imageReference(target);
    if (!alt && reference === null) {
      return [];
    }
    return [{ kind: "image", text: alt, id: reference, href: href }];
  }

  function markdownSpans(value, depth, images) {
    if (depth >= SPAN_DEPTH) {
      return value ? [{ kind: "text", text: value }] : [];
    }
    var pattern = new RegExp(INLINE.source, INLINE.flags);
    var spans = [];
    var position = 0;
    var match;
    while ((match = pattern.exec(value)) !== null) {
      if (match.index > position) {
        spans.push({ kind: "text", text: value.slice(position, match.index) });
      }
      position = match.index + match[0].length;
      var groups = match.groups;
      if (groups.alt !== undefined) {
        if (images) {
          spans = spans.concat(imageSpans(groups.alt, groups.target, null));
        } else if (groups.alt) {
          spans.push({ kind: "text", text: groups.alt });
        }
      } else if (groups.code !== undefined) {
        spans.push({ kind: "code", text: groups.code });
      } else if (groups.label !== undefined) {
        var url = webUrl(groups.url) ? groups.url : null;
        // A link whose whole label is an image becomes that image, and the image keeps the link.
        var linked = images ? IMAGE_ONLY.exec(groups.label.trim()) : null;
        var inside = linked ? imageSpans(linked.groups.alt, linked.groups.target, url) : [];
        spans = spans.concat(inside.length ? inside
          : [{ kind: "link", url: url, spans: markdownSpans(groups.label, depth + 1) }]);
      } else {
        var strong = groups.strong !== undefined ? groups.strong : groups.strongScore;
        var inner = strong !== undefined ? strong : (groups.emphasis !== undefined ? groups.emphasis : groups.emphasisScore);
        spans.push({ kind: strong !== undefined ? "strong" : "emphasis", spans: markdownSpans(inner, depth + 1) });
      }
    }
    if (position < value.length) {
      spans.push({ kind: "text", text: value.slice(position) });
    }
    return spans;
  }

  function markdownBlocks(value) {
    var blocks = [];
    var paragraph = [];
    var lines = value.split("\n");
    var index = 0;

    function close(run) {
      if (run.some(function (span) { return span.kind !== "text" || span.text.trim(); })) {
        blocks.push({ kind: "paragraph", spans: run });
      }
    }

    // One paragraph per run of spans, with a block for every image between them, so two images never merge into one sentence.
    function add(spans) {
      var run = [];
      spans.forEach(function (span) {
        if (span.kind !== "image") {
          run.push(span);
          return;
        }
        close(run);
        run = [];
        blocks.push({ kind: "image", text: span.text, id: span.id, href: span.href });
      });
      close(run);
    }

    function flush() {
      if (paragraph.length) {
        add(markdownSpans(paragraph.join(" "), 0, true));
        paragraph = [];
      }
    }

    while (index < lines.length) {
      var line = lines[index];
      if (line.indexOf(FENCE) === 0) {
        flush();
        index++;
        var code = [];
        while (index < lines.length && lines[index].indexOf(FENCE) !== 0) {
          code.push(lines[index]);
          index++;
        }
        index++;
        blocks.push({ kind: "code", text: code.join("\n") });
        continue;
      }
      var heading = HEADING.exec(line);
      var item = heading ? null : ITEM.exec(line);
      if (heading) {
        flush();
        blocks.push({ kind: "heading", level: heading[1].length, spans: markdownSpans(heading[2], 0) });
      } else if (item) {
        flush();
        var ordered = /\d/.test(item[1].charAt(0));
        var last = blocks[blocks.length - 1];
        if (!last || last.kind !== "list" || last.ordered !== ordered) {
          last = { kind: "list", ordered: ordered, items: [] };
          blocks.push(last);
        }
        last.items.push(markdownSpans(item[2], 0));
      } else if (line.trim()) {
        paragraph.push(line.trim());
      } else {
        flush();
      }
      index++;
    }

    flush();
    return blocks;
  }

  function statusNotice(entry) {
    var state = stateOf(entry);
    if (state === null || state === "delisted") {
      return null;
    }
    var reason = text(entry.index_status.reason);
    var label = text(state) || "unknown";
    return { text: "The index marks this as " + label + "." + (reason ? " " + reason : "") };
  }

  function newestRelease(releases) {
    var usable = releases.filter(function (release) { return isObject(release) && release.yanked !== true; });
    var stable = usable.filter(function (release) { return release.release_status === undefined || release.release_status === "stable"; });
    return stable[0] || usable[0] || null;
  }

  function channelOf(release) {
    var status = release.release_status;
    if (status === undefined || status === null) {
      return CHANNELS.stable;
    }
    var channel = CHANNELS[String(status).toLowerCase()];
    return channel === undefined ? DEV_CHANNEL : channel;
  }

  function forumLink(authored) {
    if (!isObject(authored.links)) {
      return null;
    }
    var keys = Object.keys(authored.links).filter(function (key) { return key.toLowerCase() === "forums"; });
    for (var i = 0; i < keys.length; i++) {
      if (webUrl(authored.links[keys[i]])) {
        return authored.links[keys[i]];
      }
    }
    return null;
  }

  // The forum line of one pin and its newer release, the way the share page generator writes them.
  function packMember(pin, snapshot) {
    if (!isObject(pin) || !validId(pin.id) || !text(pin.version)) {
      return null;
    }
    var version = text(pin.version);
    var entry = (Array.isArray(snapshot.listings) ? snapshot.listings : []).filter(function (candidate) {
      return isObject(candidate) && validId(candidate.id) && candidate.id.toLowerCase() === pin.id.toLowerCase()
        && isObject(candidate.authored) && stateOf(candidate) !== "delisted";
    })[0];
    var releases = entry && Array.isArray(entry.releases) ? entry.releases.filter(isObject) : [];
    var index = -1;
    for (var i = 0; i < releases.length && index < 0; i++) {
      if (text(releases[i].version) === version) {
        index = i;
      }
    }
    if (index < 0) {
      return { line: pin.id + " " + version + " - Not listed in the content index", newer: null };
    }
    var authored = entry.authored;
    var name = text(authored.name) || entry.id;
    var download = isObject(releases[index].download) && webUrl(releases[index].download.url) ? releases[index].download.url : null;
    var line = [
      name + " " + version,
      "Author: " + (authorsOf(authored).join(", ") || "not stated"),
      "License: " + (text(authored.license) || "not stated"),
      "Download: " + (download || "not stated"),
      "Thread: " + (forumLink(authored) || "not stated")
    ].join(" - ");
    var channel = channelOf(releases[index]);
    // The releases are in descending SemVer precedence, so every release before the pinned one is newer.
    var newer = releases.slice(0, index).filter(function (release) {
      return release.yanked !== true && channelOf(release) <= channel && text(release.version);
    })[0];
    return { line: line, newer: newer ? name + " " + text(newer.version) : null };
  }

  function newerText(view) {
    if (!view.newer.length) {
      return null;
    }
    var total = view.forumList.length;
    var head = view.newer.length === 1 ? "1 of " + total + " mods has a newer release"
      : view.newer.length + " of " + total + " mods have newer releases";
    return head + ": " + view.newer.join(", ") + ".";
  }

  function findEntry(entries, id) {
    var lower = id.toLowerCase();
    for (var i = 0; Array.isArray(entries) && i < entries.length; i++) {
      if (isObject(entries[i]) && validId(entries[i].id) && entries[i].id.toLowerCase() === lower) {
        return entries[i];
      }
    }
    return null;
  }

  function listingView(entry, snapshot) {
    var authored = entry.authored;
    var versions = isObject(snapshot.game_versions) && Array.isArray(snapshot.game_versions.versions) ? snapshot.game_versions.versions : [];
    var release = newestRelease(Array.isArray(entry.releases) ? entry.releases : []);
    var game;
    if (release) {
      var hasMax = "game_max" in release || "game_max_revision" in release;
      game = gameText(gameVersion(versions, release.game_min_revision, release.game_min), hasMax ? gameVersion(versions, release.game_max_revision, release.game_max) : null);
    } else {
      var compatibility = isObject(authored.compatibility) ? authored.compatibility : {};
      game = gameText(text(compatibility.game_min), text(compatibility.game_max));
    }
    var channel = release ? text(release.release_status) : null;
    var view = {
      kind: "mod",
      id: entry.id,
      name: text(authored.name) || entry.id,
      type: TYPES[text(authored.type)] || "Mod",
      authors: authorsOf(authored),
      abstract: text(authored.abstract),
      description: richText(authored.description),
      license: text(authored.license),
      version: release ? text(release.version) : null,
      channel: channel === "stable" ? null : channel,
      date: release ? dateText(release.release_date) : null,
      game: game,
      downloads: isObject(entry.downloads) ? count(entry.downloads.total) : null,
      modCount: null,
      forumList: null,
      newer: [],
      tags: displayTags(snapshot, authored.tags),
      links: linksOf(authored),
      images: imageRecords(authored),
      notices: []
    };
    var notice = statusNotice(entry);
    if (notice) {
      view.notices.push(notice);
    }
    if (authored.status === "deprecated") {
      var successor = authored.superseded_by;
      var known = validId(successor) ? findEntry(snapshot.listings, successor) : null;
      if (known && isObject(known.authored)) {
        view.notices.push({ text: "Deprecated by its author. Its successor is", link: { label: text(known.authored.name) || known.id, id: known.id } });
      } else {
        view.notices.push({ text: validId(successor) ? "Deprecated by its author. Its successor is " + successor + "." : "Deprecated by its author." });
      }
    }
    return view;
  }

  function packView(entry, snapshot) {
    var versions = (Array.isArray(entry.versions) ? entry.versions : []).filter(function (version) { return isObject(version) && isObject(version.authored); });
    if (!versions.length) {
      return null;
    }
    var current = versions.filter(function (version) { return stateOf(version) !== "retracted"; })[0] || null;
    var authored = (current || versions[0]).authored;
    var compatibility = isObject(authored.compatibility) ? authored.compatibility : {};
    var view = {
      kind: "pack",
      id: entry.id,
      name: text(authored.name) || entry.id,
      type: TYPES.modpack,
      authors: authorsOf(authored),
      abstract: text(authored.abstract),
      description: richText(authored.description),
      license: text(authored.license),
      version: text(authored.version),
      channel: null,
      date: dateText(authored.released_at),
      game: gameText(text(compatibility.game_min), text(compatibility.game_max)),
      downloads: null,
      modCount: Array.isArray(authored.mods) ? authored.mods.length : null,
      forumList: [],
      newer: [],
      tags: displayTags(snapshot, authored.tags),
      links: linksOf(authored),
      images: imageRecords(authored),
      notices: []
    };
    (Array.isArray(authored.mods) ? authored.mods : []).forEach(function (pin) {
      var member = packMember(pin, snapshot);
      if (member) {
        view.forumList.push(member.line);
        if (member.newer) {
          view.newer.push(member.newer);
        }
      }
    });
    var notice = statusNotice(entry);
    if (notice) {
      view.notices.push(notice);
    }
    if (!current) {
      view.notices.push({ text: "Every version of this pack is retracted." });
    }
    if (authored.status === "deprecated") {
      view.notices.push({ text: "Deprecated by its author." });
    }
    return view;
  }

  function element(tag, className, content) {
    var node = document.createElement(tag);
    if (className) {
      node.className = className;
    }
    if (content !== undefined && content !== null) {
      node.textContent = content;
    }
    return node;
  }

  function link(href, label, className) {
    var node = element("a", className, label);
    node.href = href;
    return node;
  }

  function chips(values) {
    var list = element("ul", "chips");
    values.forEach(function (value) {
      list.appendChild(element("li", "chip", value));
    });
    return list;
  }

  function spanNodes(node, spans) {
    spans.forEach(function (span) {
      if (span.kind === "text" || span.kind === "image") {
        node.appendChild(document.createTextNode(span.text));
        return;
      }
      if (span.kind === "code") {
        node.appendChild(element("code", null, span.text));
        return;
      }
      var inner = spanNodes(document.createDocumentFragment(), span.spans);
      if (!inner.childNodes.length) {
        return;
      }
      if (span.kind === "strong" || span.kind === "emphasis") {
        var emphasis = element(span.kind === "strong" ? "strong" : "em");
        emphasis.appendChild(inner);
        node.appendChild(emphasis);
      } else if (span.url) {
        var anchor = element("a");
        anchor.href = span.url;
        anchor.rel = "nofollow noopener";
        anchor.appendChild(inner);
        node.appendChild(anchor);
      } else {
        node.appendChild(inner);
      }
    });
    return node;
  }

  // The frame is never wider than the image itself, so a small image keeps its own pixels instead of being
  // stretched, and never taller than the height a screen has room for.
  function figureStyle(record) {
    return "aspect-ratio: " + record.width + " / " + record.height +
      "; max-width: min(" + record.width + "px, calc(var(--shot-height) * " + record.width + " / " + record.height + "))";
  }

  function figureCredit(record) {
    if (!record.attribution && !record.source) {
      return null;
    }
    var credit = element("span", "credit");
    if (record.attribution) {
      credit.appendChild(document.createTextNode(record.attribution));
    }
    if (record.source) {
      if (record.attribution) {
        credit.appendChild(document.createTextNode(" " + DOT + " "));
      }
      var anchor = link(record.source, "Source");
      anchor.rel = "nofollow noopener";
      credit.appendChild(anchor);
    }
    return credit;
  }

  function figureNode(block, record) {
    var caption = null;
    if (block.text) {
      caption = block.href ? link(block.href, block.text) : document.createTextNode(block.text);
      if (block.href) {
        caption.rel = "nofollow noopener";
      }
    }
    if (!record) {
      // An image that names a record the page does not have still leaves a line behind, because a reader
      // has to see that there is an image here that the page does not show.
      if (!caption && !block.id) {
        return null;
      }
      var line = element("p", "figure");
      line.appendChild(caption || document.createTextNode(IMAGE_PLACEHOLDER));
      return line;
    }
    var figure = element("figure");
    figure.setAttribute("data-image", record.url);
    figure.setAttribute("data-sha256", record.sha256);
    figure.setAttribute("data-width", String(record.width));
    figure.setAttribute("data-height", String(record.height));
    figure.setAttribute("data-size", String(record.size));
    // The frame carries the link as well, so a reader can open it from the image, and the caption gives
    // the only link that a reader who navigates by keyboard or by screen reader meets.
    var frame = element(block.href ? "a" : "div", "frame");
    frame.setAttribute("style", figureStyle(record));
    if (block.href) {
      frame.href = block.href;
      frame.rel = "nofollow noopener";
      frame.setAttribute("tabindex", "-1");
      frame.setAttribute("aria-hidden", "true");
    }
    figure.appendChild(frame);
    var credit = figureCredit(record);
    if (caption || credit) {
      var below = element("figcaption");
      if (caption) {
        below.appendChild(caption);
      }
      if (credit) {
        below.appendChild(credit);
      }
      figure.appendChild(below);
    } else {
      // An image with no words of its own still gets a caption, because the caption is the whole of what
      // a reader sees when the image does not arrive. The page hides it again once the image is there.
      figure.appendChild(element("figcaption", "untitled", IMAGE_PLACEHOLDER));
    }
    return figure;
  }

  // RFC 0058 asks a client to offer a reader a way to load no image from the host of an author. A share page
  // goes further and loads none until the reader asks, because a reader arrives here from a link and never
  // chose to tell a host their address. The page carries the switch and description-images.js drives it.
  function imageSwitch() {
    var line = element("p", "image-switch");
    var label = element("label");
    var box = element("input");
    box.setAttribute("type", "checkbox");
    label.appendChild(box);
    label.appendChild(document.createTextNode(" Show images from author hosts"));
    line.appendChild(label);
    line.appendChild(element("span", "meta",
      "Every image comes from the host of its author, which then learns your address."));
    return line;
  }

  function markdownNodes(markdown, images) {
    var fragment = document.createDocumentFragment();
    markdownBlocks(markdown).forEach(function (block) {
      if (block.kind === "heading") {
        fragment.appendChild(spanNodes(element("h" + Math.min(block.level + HEADING_OFFSET, 6)), block.spans));
      } else if (block.kind === "code") {
        var pre = element("pre");
        pre.appendChild(element("code", null, block.text));
        fragment.appendChild(pre);
      } else if (block.kind === "image") {
        var figure = figureNode(block, (images && images[block.id]) || null);
        if (figure) {
          fragment.appendChild(figure);
        }
      } else if (block.kind === "list") {
        var list = element(block.ordered ? "ol" : "ul");
        block.items.forEach(function (item) {
          list.appendChild(spanNodes(element("li"), item));
        });
        fragment.appendChild(list);
      } else {
        fragment.appendChild(spanNodes(element("p"), block.spans));
      }
    });
    return fragment;
  }

  function members(view) {
    var section = element("section", "members");
    section.appendChild(element("h2", null, "Mods"));
    var summary = newerText(view);
    if (summary) {
      section.appendChild(element("p", "newer", summary));
    }
    section.appendChild(element("pre", "forum-list", view.forumList.join("\n")));
    var button = element("button", "button secondary", "Copy forum list");
    button.setAttribute("type", "button");
    button.setAttribute("data-copy-list", "");
    button.hidden = true;
    section.appendChild(button);
    section.appendChild(element("p", "meta",
      "One line per mod with its version, author, license, download and release thread, the way the forum rules ask a pack thread to list them."));
    return section;
  }

  function actionNodes(view, base) {
    var fragment = document.createDocumentFragment();
    var actions = element("div", "actions");
    actions.appendChild(link("borea://" + view.kind + "/" + view.id, "Open in Borea", "button desktop-only"));
    actions.appendChild(link("borea://install/" + view.id, "Install with Borea", "button secondary desktop-only"));
    var copy = element("button", "button handheld-only", "Copy link");
    copy.setAttribute("type", "button");
    copy.setAttribute("data-copy-link", "");
    copy.hidden = true;
    actions.appendChild(copy);
    actions.appendChild(link(base + "#download", "Get Borea", "button secondary"));
    fragment.appendChild(actions);
    fragment.appendChild(element("p", "meta hint desktop-only", "Open in Borea and Install with Borea need Borea on this computer."));
    fragment.appendChild(element("p", "meta hint handheld-only",
      "Borea runs on Windows, Linux and macOS. To install this with Borea, open this page on a computer."));
    return fragment;
  }

  function render(view) {
    var header = element("div", "listing listing-header");
    var icon = element("div", "listing-icon placeholder");
    var image = document.createElement("img");
    image.src = root + "img/placeholder.svg";
    image.width = 56;
    image.height = 56;
    image.alt = "";
    icon.appendChild(image);
    header.appendChild(icon);

    var intro = element("div", "listing-intro");
    intro.appendChild(element("p", "meta kind", view.type));
    intro.appendChild(element("h1", null, view.name));
    if (view.authors.length) {
      intro.appendChild(element("p", "meta by", "by " + view.authors.join(", ")));
    }
    if (view.abstract) {
      intro.appendChild(element("p", "tagline", view.abstract));
    }
    if (view.tags.length) {
      intro.appendChild(chips(view.tags));
    }
    view.notices.forEach(function (notice) {
      var line = element("p", "notice", notice.text);
      line.setAttribute("role", "note");
      if (notice.link) {
        line.appendChild(document.createTextNode(" "));
        line.appendChild(link(root + "mod/" + notice.link.id + "/", notice.link.label));
        line.appendChild(document.createTextNode("."));
      }
      intro.appendChild(line);
    });
    intro.appendChild(actionNodes(view, root));
    header.appendChild(intro);

    var body = element("div", "listing-body");
    var description = element("section", "description");
    description.appendChild(element("h2", null, "Description"));
    var prose = element("div", "prose");
    prose.appendChild(view.description ? markdownNodes(view.description, view.images) : element("p", "meta", "No description provided."));
    if (prose.querySelector("figure[data-image]")) {
      description.appendChild(imageSwitch());
    }
    description.appendChild(prose);
    if (view.forumList) {
      var main = element("div", "listing-main");
      main.appendChild(description);
      main.appendChild(members(view));
      body.appendChild(main);
    } else {
      body.appendChild(description);
    }

    var panel = element("aside", "panel");
    if (view.game) {
      var compatibility = element("section");
      compatibility.appendChild(element("h2", null, "Compatibility"));
      compatibility.appendChild(chips([view.game]));
      panel.appendChild(compatibility);
    }
    if (view.links.length) {
      var section = element("section");
      section.appendChild(element("h2", null, "Links"));
      var list = element("ul", "links");
      view.links.forEach(function (item) {
        var row = element("li");
        var anchor = link(item.url.href, item.label);
        anchor.rel = "nofollow noopener";
        row.appendChild(anchor);
        row.appendChild(document.createTextNode(" "));
        row.appendChild(element("span", "meta", item.url.hostname));
        list.appendChild(row);
      });
      section.appendChild(list);
      panel.appendChild(section);
    }

    var details = element("section");
    details.appendChild(element("h2", null, "Details"));
    var facts = element("dl", "facts");
    function fact(name, value) {
      facts.appendChild(element("dt", null, name));
      var cell = element("dd");
      if (typeof value === "string") {
        cell.textContent = value;
      } else {
        cell.appendChild(value);
      }
      facts.appendChild(cell);
    }
    fact("Type", view.type);
    if (view.version) {
      var version = document.createDocumentFragment();
      version.appendChild(document.createTextNode(view.version));
      if (view.channel) {
        version.appendChild(document.createTextNode(" "));
        version.appendChild(element("span", "channel", view.channel));
      }
      if (view.date) {
        version.appendChild(document.createTextNode(" " + DOT + " " + view.date));
      }
      fact("Latest version", version);
    } else {
      fact("Latest version", "No release yet");
    }
    if (view.modCount !== null) {
      fact("Mods", String(view.modCount));
    }
    if (view.downloads !== null) {
      fact("Downloads", view.downloads.toLocaleString("en-US"));
    }
    if (view.license) {
      fact("License", view.license);
    }
    details.appendChild(facts);
    panel.appendChild(details);
    body.appendChild(panel);

    page.textContent = "";
    page.className = "column";
    page.appendChild(header);
    page.appendChild(body);
    document.title = view.name + " - Borea";
    document.body.classList.add("share");
    loading.hidden = true;
    page.hidden = false;
    // The same script the static pages use, so both show a description image the same way.
    if (window.boreaImages) {
      window.boreaImages.start(description);
    }
    if (window.boreaCopyList) {
      window.boreaCopyList.start(page);
    }
  }

  function showNotFound(message) {
    if (message) {
      document.getElementById("not-found-text").textContent = message;
    }
    loading.hidden = true;
    notFound.hidden = false;
  }

  // The parity test in .github/scripts/tests loads this file in Node.
  if (typeof document === "undefined") {
    module.exports = { listingView: listingView, packView: packView, markdownBlocks: markdownBlocks, markdownNodes: markdownNodes, actionNodes: actionNodes };
    return;
  }

  var root = new URL(document.baseURI).pathname;
  var found = new RegExp("^" + root.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "(mod|pack)/([^/]+)/?$").exec(location.pathname);
  var notFound = document.getElementById("not-found");
  var loading = document.getElementById("loading");
  var page = document.getElementById("page");
  var id = null;
  try {
    id = found ? decodeURIComponent(found[2]) : null;
  } catch (error) {
    id = null;
  }
  if (!validId(id)) {
    return;
  }

  var kind = found[1];
  notFound.hidden = true;
  loading.hidden = false;

  fetch(SNAPSHOT)
    .then(function (response) {
      if (!response.ok) {
        throw new Error("HTTP " + response.status);
      }
      return response.json();
    })
    .then(function (snapshot) {
      if (!isObject(snapshot) || snapshot.snapshot_version !== 1) {
        throw new Error("unknown snapshot");
      }
      var entry = findEntry(kind === "mod" ? snapshot.listings : snapshot.packs, id);
      if (!entry) {
        showNotFound("The content index has no " + kind + " with this id.");
        return;
      }
      if (stateOf(entry) === "delisted") {
        showNotFound("This content was removed from the content index.");
        return;
      }
      if (entry.id !== id) {
        location.replace(root + kind + "/" + entry.id + "/");
        return;
      }
      var view = kind === "mod" ? (isObject(entry.authored) ? listingView(entry, snapshot) : null) : packView(entry, snapshot);
      if (view) {
        render(view);
      } else {
        showNotFound();
      }
    })
    .catch(function () {
      showNotFound("The content index could not be loaded. Try again later.");
    });
})();
