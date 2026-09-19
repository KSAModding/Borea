// Renders the share page of a listing that is newer than the last site build, from the snapshot.
// Authors write the snapshot, so its values reach the page only through textContent and checked URLs.
(function () {
  "use strict";

  var SNAPSHOT = "https://ksamodding.github.io/content-index-releases/v1/index.json";
  var ID = /^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/;
  var RESERVED = /^(core|con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;
  var CONTROL = /[\x00-\x1f\x7f]/g;
  var DOT = String.fromCharCode(0xb7);
  // No prototype, so an author key such as "constructor" finds nothing.
  var TYPES = Object.assign(Object.create(null), { mod: "Mod", "mod-loader": "Mod loader", modpack: "Modpack" });
  var LINKS = Object.assign(Object.create(null), { forums: "Forum", repository: "Repository", spacedock: "SpaceDock", bugtracker: "Bug tracker", homepage: "Homepage", discussions: "Discussions" });
  var LINK_ORDER = Object.keys(LINKS);
  var MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

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
      license: text(authored.license),
      version: release ? text(release.version) : null,
      channel: channel === "stable" ? null : channel,
      date: release ? dateText(release.release_date) : null,
      game: game,
      downloads: isObject(entry.downloads) ? count(entry.downloads.total) : null,
      modCount: null,
      links: linksOf(authored),
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

  function packView(entry) {
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
      license: text(authored.license),
      version: text(authored.version),
      channel: null,
      date: dateText(authored.released_at),
      game: gameText(text(compatibility.game_min), text(compatibility.game_max)),
      downloads: null,
      modCount: Array.isArray(authored.mods) ? authored.mods.length : null,
      links: linksOf(authored),
      notices: []
    };
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

  function render(view) {
    var hero = element("div", "hero listing");
    var icon = element("div", "listing-icon placeholder");
    var image = document.createElement("img");
    image.src = root + "img/placeholder.svg";
    image.width = 56;
    image.height = 56;
    image.alt = "";
    icon.appendChild(image);
    hero.appendChild(icon);
    hero.appendChild(element("p", "meta kind", view.type));
    hero.appendChild(element("h1", null, view.name));
    if (view.authors.length) {
      hero.appendChild(element("p", "meta by", "by " + view.authors.join(", ")));
    }
    if (view.abstract) {
      hero.appendChild(element("p", "tagline", view.abstract));
    }
    view.notices.forEach(function (notice) {
      var line = element("p", "notice", notice.text);
      line.setAttribute("role", "note");
      if (notice.link) {
        line.appendChild(document.createTextNode(" "));
        line.appendChild(link(root + "mod/" + notice.link.id + "/", notice.link.label));
        line.appendChild(document.createTextNode("."));
      }
      hero.appendChild(line);
    });
    var actions = element("div", "actions");
    actions.appendChild(link("borea://" + view.kind + "/" + view.id, "Open in Borea", "button"));
    actions.appendChild(link("borea://install/" + view.id, "Install with Borea", "button secondary"));
    actions.appendChild(link(root + "#download", "Get Borea", "button secondary"));
    hero.appendChild(actions);
    hero.appendChild(element("p", "meta hint", "Open in Borea and Install with Borea need Borea on this computer."));

    var column = element("div", "column");
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
    if (view.game) {
      fact("Game", view.game);
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
    column.appendChild(details);

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
      column.appendChild(section);
    }

    page.textContent = "";
    page.appendChild(hero);
    page.appendChild(column);
    document.title = view.name + " - Borea";
    loading.hidden = true;
    page.hidden = false;
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
    module.exports = { listingView: listingView, packView: packView };
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
      var view = kind === "mod" ? (isObject(entry.authored) ? listingView(entry, snapshot) : null) : packView(entry);
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
