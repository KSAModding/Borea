# Borea

[English](README.md) | [简体中文](README.zh-CN.md) | Deutsch

> Dies ist eine Übersetzung der englischen README und kann gegenüber der englischen Fassung veraltet sein. Bei Abweichungen gilt die englische Fassung.

![Downloads](https://img.shields.io/github/downloads/KSAModding/Borea/total)
![Lizenz](https://img.shields.io/badge/License-MIT-green.svg)
![Letzter Commit](https://img.shields.io/github/last-commit/KSAModding/Borea/main.svg)
![GitHub Release (neuestes nach Datum)](https://img.shields.io/github/v/release/KSAModding/Borea)
[![Übersetzungsstand](https://hosted.weblate.org/widget/borea/svg-badge.svg)](https://hosted.weblate.org/engage/borea/)


<img src="assets/borea.png" alt="Das Borea-Symbol, ein Polarlicht über einem dunklen Planeten" width="128" height="128">

Borea ist ein plattformübergreifender Content-Manager für Kitten Space Agency.

Borea soll Mods, Modpacks, Fahrzeuge, Spielstände und mehr verwalten.
Mods und Modpacks funktionieren schon heute, Fahrzeuge und Spielstände kommen später dazu.
Borea installiert sie aus dem Content-Index der Community, verwaltet sie in getrennten Instanzen und startet das Spiel mit der Instanz, die du auswählst.
Es läuft unter Windows, Linux und macOS, als Desktop-App und als Kommandozeilenprogramm.

Borea ist ein Community-Projekt von [KSA Modding](https://github.com/KSAModding), dem Team hinter dem [KSA Modding Society Discord](https://discord.gg/nt4fK4QuTz), dem [Modding-Wiki](https://modding.kittenspaceagency.wiki/) und dem [Content-Index](https://github.com/KSAModding/content-index), aus dem Borea installiert. Im Discord findet das Modding von Kitten Space Agency statt, und alle drei werden von denselben Leuten betreut.
Borea stammt nicht von RocketWerkz, steht in keiner Verbindung zu RocketWerkz und wird von RocketWerkz weder unterstützt noch empfohlen.
Kitten Space Agency ist das Spiel und die Marke von RocketWerkz.

![Die Entdecken-Seite von Borea](docs/images/discover.png)

## Hilfe

- Fragen und Hilfe: der [KSA Modding Society Discord](https://discord.gg/nt4fK4QuTz).
- Fehler: der [Issue-Tracker](https://github.com/KSAModding/Borea/issues).
- Zum Mithelfen siehe [CONTRIBUTING.md](CONTRIBUTING.md), für Sicherheitsprobleme [SECURITY.md](SECURITY.md).

## Was Borea kann

- Den [Content-Index](https://github.com/KSAModding/content-index) durchsuchen und Mods mit einem Klick installieren. Abhängigkeiten werden aufgelöst, Empfehlungen angeboten, und der Mod-Loader wird installiert, wenn ein Start ihn braucht.
- Mehrere Instanzen des Spiels anlegen, jede mit eigenen Mods, Spielständen und Fahrzeugen, und zwischen ihnen wechseln. Das eigene Profil des Spiels bleibt unangetastet.
- Mods aktualisieren, sobald eine neue Version erscheint, und Borea selbst über das Banner auf der Startseite oder mit `borea update-self` aktualisieren.
- Modpacks installieren und eine Mod-Liste exportieren oder importieren, um ein Setup zu teilen.
- Das Spiel über den Mod-Loader mit der richtigen Instanz starten und sehen, warum ein Start fehlgeschlagen ist, inklusive des Mods, der dafür verantwortlich ist, und einer Schaltfläche, um ihn zu deaktivieren.
- Von Hand installierte Mods in eine Instanz übernehmen.
- Spielzeit und letzte Spielsitzung pro Instanz, das Spiel-Log in der App und eine Aufgaben-Seite, die zeigt, was Borea getan hat.
- Englisch, Deutsch, Piratensprache und vereinfachtes Chinesisch, dazu ein dunkles und ein helles Design.

Alles, was Borea über einen Mod weiß, kommt aus dem Content-Index, den die RFCs in [content-manager-design](https://github.com/KSAModding/content-manager-design) festlegen.
Der Index besteht aus zwei Repositories: [content-index](https://github.com/KSAModding/content-index) enthält die Listings, die Mod-Autoren schreiben, und [content-index-releases](https://github.com/KSAModding/content-index-releases) die Release-Dateien, die für jede neue Veröffentlichung erzeugt werden.
Um einen Mod in den Index aufzunehmen, genügt ein Pull Request mit einer TOML-Datei in content-index.
Wenn dein Mod Dateien unter `Documents/My Games/Kitten Space Agency` liest, siehe [Make your mod work in a Borea instance](docs/mod-authors.md).

## Downloads

Jedes Release hat zwei Archive pro Plattform.
Das App-Archiv enthält ein Programm, `borea` (`borea.exe` unter Windows).
Ohne Argumente gestartet, öffnet es die Desktop-App.
Pro Benutzer läuft nur eine App, ein zweiter Start holt also das offene Fenster nach vorne.
Mit Argumenten gestartet, führt es einen Befehl aus, zum Beispiel `borea --help`.
Mit einem `borea://`-Link gestartet, etwa `borea://mod/<id>`, öffnet es diese Seite in der App, und die App registriert sich selbst für solche Links.
Für die meisten ist das das richtige Archiv.
Das CLI-Archiv enthält nur die Kommandozeile.
Es ist der kleinere Download für Skripte und für Rechner ohne Desktop.

Die Builds enthalten die .NET-Laufzeit und sind self-contained, du musst also vorher nichts installieren.

Jedes Archiv enthält außerdem `LICENSE` und `THIRD-PARTY-NOTICES.txt` mit den Lizenzen der enthaltenen Fremdsoftware.
Das App-Archiv entpackt sich in einen Ordner namens `Borea`, unter macOS in `Borea.app`, das CLI-Archiv in `Borea-Cli`.
Leg den Ordner dorthin, wo er bleiben soll, zum Beispiel `%LocalAppData%\Programs\Borea` unter Windows, `~/.local/share/Borea` unter Linux oder `/Applications` unter macOS, denn Borea aktualisiert sich an Ort und Stelle, also in dem Ordner, aus dem es gestartet wurde.
Auf einem Mac-Konto ohne Administratorrechte gehört `Borea.app` stattdessen nach `~/Applications`, weil Borea sich in `/Applications` dort nicht selbst aktualisieren kann.

| Plattform | App und CLI | nur CLI |
| --- | --- | --- |
| Windows | `Borea-<version>-win-x64.zip` | `Borea-Cli-<version>-win-x64.zip` |
| Linux | `Borea-<version>-linux-x64.tar.gz` | `Borea-Cli-<version>-linux-x64.tar.gz` |
| macOS, Apple Silicon | `Borea-<version>-macos-arm64.tar.gz` | `Borea-Cli-<version>-macos-arm64.tar.gz` |

Die Builds sind nicht signiert, daher braucht der erste Start jeder neuen Version unter Windows und macOS einen zusätzlichen Schritt.
Wie Releases gebaut werden, was signiert werden wird und wie du einen Download prüfst, steht in der [Code-Signing-Policy](https://ksamodding.github.io/Borea/code-signing.html), und die Arbeit dorthin wird in [Issue #678](https://github.com/KSAModding/Borea/issues/678) verfolgt.

### Windows

1. Dein Browser warnt dich vielleicht, dass die Datei selten heruntergeladen wird. Behalte sie trotzdem.
2. Klicke vor dem Entpacken mit der rechten Maustaste auf die ZIP-Datei, öffne **Eigenschaften**, setze auf der Registerkarte **Allgemein** das Häkchen bei **Zulassen** und bestätige mit **OK**.
3. Entpacke die ZIP-Datei und starte `borea.exe`. Für die Kommandozeile führe `.\borea.exe --help` in PowerShell im entpackten Ordner eines der beiden Archive aus.
4. Wenn du Schritt 2 übersprungen hast, zeigt Windows "Der Computer wurde durch Windows geschützt". Grund ist, dass die App selten heruntergeladen und nicht signiert ist. Wähle **Weitere Informationen**, dann **Trotzdem ausführen**.
5. Wenn Windows meldet, dass die intelligente App-Steuerung Borea blockiert hat, gibt es kein "Trotzdem ausführen". Gehe zurück zu Schritt 2, gib die ZIP-Datei frei und entpacke sie erneut in einen neuen Ordner.

Die Warnung kommt bei jeder neuen Version wieder.
Borea braucht keine Administratorrechte.

Unter Windows 11 24H2 und neuer öffnet sich die App ohne Konsolenfenster.
Unter älteren Windows-Versionen öffnet sich kurz ein Konsolenfenster, das sich beim Start der App schließt.
Wenn du `borea.exe` aus dem CLI-Archiv per Doppelklick startest, weist es dich auf ein Terminal hin und wartet auf die Eingabetaste.

### Linux

Entpacke das App-Archiv und starte `borea`, zum Beispiel mit `./borea` in einem Terminal im entpackten Ordner.
Nach diesem ersten Start steht Borea im Anwendungsmenü deines Desktops und startet von dort ohne Terminal.
Für die Kommandozeile führe `./borea --help` im entpackten Ordner eines der beiden Archive aus.
Der Build bringt die .NET-Laufzeit mit, aber nicht die Systembibliotheken, auf denen sie aufsetzt.
App und CLI brauchen beide die Bibliotheken ICU und OpenSSL.
Nur die App braucht zusätzlich X11, ICE, SM und fontconfig.
Normale Desktop-Installationen bringen das alles schon mit. Eine minimale Installation braucht für die App diese Pakete:

- Debian und Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libssl3` sowie das `libicu`-Paket deiner Version, zum Beispiel `libicu76`.
- Fedora: `sudo dnf install libX11 libICE libSM fontconfig libicu openssl-libs`.

Für die CLI allein genügen `libssl3` und das `libicu`-Paket unter Debian und Ubuntu, oder `libicu openssl-libs` unter Fedora.

Für die App-Oberfläche auf vereinfachtem Chinesisch braucht das System zusätzlich eine Schrift mit chinesischen Glyphen, etwa Noto Sans CJK SC oder WenQuanYi Micro Hei. Unter Debian und Ubuntu installierst du dafür `fonts-noto-cjk` oder das kleinere Paket `fonts-wqy-microhei`. Die mitgelieferten IBM-Plex-Schriften enthalten keine chinesischen Glyphen, Borea verlässt sich auf den Schrift-Fallback des Systems. Ohne passende Schrift erscheint chinesischer Text als leere Kästchen. Für die CLI wird diese Schrift nicht gebraucht.

Der Build braucht glibc, Distributionen auf musl-Basis wie Alpine werden daher nicht unterstützt.

### macOS

Intel-Macs werden nicht unterstützt.

Doppelklicke im Finder auf das App-Archiv, um es zu entpacken, ziehe `Borea.app` in deinen Ordner **Programme** und öffne es von dort.

macOS blockiert diesen ersten Start möglicherweise, weil der Build nicht mit einem Apple-Entwicklerkonto signiert ist.
Es meldet, dass Apple nicht überprüfen konnte, ob Borea frei von Schadsoftware ist.
Der vollständige Quellcode von Borea liegt in diesem Repository, du kannst ihn also selbst prüfen.

1. Wähle **Fertig**. Wähle niemals **In den Papierkorb legen**, denn das löscht Borea.
2. Öffne die **Systemeinstellungen**, gehe zu **Datenschutz & Sicherheit** und scrolle nach unten zu **Sicherheit**. Dort wird Borea genannt und **Trotzdem öffnen** angeboten.
3. Bestätige mit deinem Passwort oder per Touch ID und öffne Borea danach erneut. Es startet.

Statt der drei Schritte kannst du die Download-Markierung im Terminal entfernen: `xattr -dr com.apple.quarantine /Applications/Borea.app`.
So oder so machst du das einmal pro Version.

Die Kommandozeile liegt unter `Borea.app/Contents/MacOS/borea`.
Das Archiv `Borea-Cli-` entpackt sich mit `tar -xzf` nach `Borea-Cli`, und dessen `borea` braucht dieselben Schritte beim ersten Start, oder `xattr -dr com.apple.quarantine Borea-Cli`.

### Borea aktualisieren

**Jetzt aktualisieren** im Banner der Startseite oder `borea update-self` installiert das neueste Release deines Update-Kanals in denselben Ordner, Verknüpfungen funktionieren also weiter.
Unter macOS kann sich ein Build von vor der Umstellung auf das App-Bundle nicht auf ein Release als App-Bundle aktualisieren. Lade das App-Archiv dann einmal von Hand herunter, ziehe `Borea.app` nach Programme und lösche den alten Ordner `Borea`.
Wenn Borea nach einem Update nicht startet, benenne `borea.exe.old` (`borea.old` unter Linux, `Borea.app.old` unter macOS) wieder in den alten Namen um, um den vorherigen Build zurückzubekommen.

### Prüfsummen und Herkunft

`SHA256SUMS.txt` in jedem Release listet die Prüfsumme jedes Archivs und jeder Software-Stückliste.

GitHub hält außerdem für jede veröffentlichte Datei eine Build-Provenance-Attestierung, die sie mit dem Workflow-Lauf verknüpft, der sie gebaut hat.

Mit angemeldetem `gh` prüfst du eine davon so:

```sh
gh attestation verify <file> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml
```

`Borea-<version>.cdx.json` ist die Software-Stückliste der App-Archive, im Format CycloneDX JSON.
Die App enthält die Kommandozeile, diese Datei listet also die NuGet-Pakete der App und der CLI.
`Borea-Cli-<version>.cdx.json` ist die Software-Stückliste der CLI-Archive und listet nur die Pakete der CLI.
Jede Datei enthält Version, Lizenz und Hash jedes Pakets.
Jede Liste ist nur für die Archive attestiert, die sie beschreibt, und mit diesem Befehl kannst du nachweisen, dass sie zu einem davon gehört.
Mit `--format json` enthält die Ausgabe auch die attestierte Liste.

```sh
gh attestation verify <archive> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml \
  --predicate-type https://cyclonedx.org/bom
```

## Kommandozeile

`borea` führt die Operationen von Borea aus einem Skript aus. Jeder lesende Befehl versteht `--json`.
Die Tabelle zeigt die Befehle für Einstellungen und Instanzen. `borea --help` listet alle auf, darunter search, show, install, update, remove, launch, loader und pack.
Der Exit-Code ist 0, wenn der Befehl durchgelaufen ist, 1, wenn die Operation fehlgeschlagen ist und der Grund auf stderr steht, und 2, wenn die Kommandozeile nicht geparst werden konnte.

| Befehl | Was er macht |
| --- | --- |
| `borea settings show` | Ausgeben, wo Spiel, Mod-Loader und Bibliothek liegen, welche Mod-Loader-Versionen installiert sind, welcher Release-Kanal gilt und ob der gemeinsame Mod-Speicher an ist. |
| `borea settings set game <directory>` | Borea sagen, wo das Spiel installiert ist. |
| `borea settings set loader <loader-id> <directory>` | Borea sagen, wo ein installierter Mod-Loader liegt. |
| `borea settings set channel <channel>` | Wählen, welche Release-Status Installation und Update anbieten: `stable` (Standard), `testing` oder `dev`. |
| `borea settings set library <directory>` | Instanzen und Backups in einen anderen Ordner verschieben, oder die Bibliothek benutzen, die dieser Ordner schon enthält. `--default` verschiebt sie zurück. |
| `borea update-self` | Diesen Borea-Build durch das neueste Release deines Update-Kanals ersetzen. `--check` meldet nur, welches Release installiert würde. |
| `borea settings set shared-store <on\|off>` | Jedes Mod-Release einmal speichern und jede Instanz darauf verlinken (`on`, Standard), oder jeder Instanz eine eigene Kopie geben (`off`). |
| `borea game version` | Den installierten Build und den aktuellen öffentlichen Build ausgeben, den der Master-Server meldet. |
| `borea game check` | Die Spielinstallation gegen das prüfen, was Borea über sie annimmt. Endet mit 1, wenn eine Annahme verletzt ist. |
| `borea instance list` | Jede Instanz ausgeben und die aktive markieren. |
| `borea instance show <instance>` | Eine Instanz ausgeben, dazu den Spiel-Build, auf dem sie läuft, den Mod-Loader, den ihre Mods brauchen, und die Version, die sie startet, ihre Startargumente, wann sie zuletzt gespielt wurde und wie lange. |
| `borea instance create <name>` | Eine leere Instanz anlegen. |
| `borea instance duplicate <instance> [--name <name>]` | Eine Instanz mit denselben Mods und Versionen anlegen, in der dieselben Mods aktiviert sind. |
| `borea instance export <instance> [file]` | Die Mods einer Instanz als Mod-Liste schreiben, mit Version und ob sie aktiv sind. |
| `borea instance import <file> [--name <name>]` | Eine Instanz aus einer Mod-Liste anlegen. |
| `borea instance rename <instance> <new-name>` | Einer Instanz einen neuen Namen geben. |
| `borea instance arguments <instance>` | Die Startargumente ausgeben, die jeder Start einer Instanz an Mod-Loader und Spiel übergibt. |
| `borea instance set-arguments <instance> -- <arguments>` | Die Argumente nach `--` als Startargumente einer Instanz speichern, anstelle der gespeicherten. |
| `borea instance clear-arguments <instance>` | Die Startargumente einer Instanz entfernen. |
| `borea instance set-loader <instance> <loader-id\|auto> [--version <version>\|auto]` | Den Mod-Loader setzen, den eine Instanz startet. Ein `--version` fixiert genau dieses Release, auch wenn sich das Spiel ändert. `auto` lässt Borea das neueste Release wählen, das zum Spiel und zu den Mods passt, und ist der Standard. |
| `borea instance delete <instance>` | Eine Instanz löschen, dazu ihren Ordner und die Backups ihrer Spielstände und Fahrzeuge. |
| `borea instance activate <instance>` | Eine Instanz zur aktiven machen. |
| `borea instance deactivate` | Keine Instanz aktiv lassen. |
| `borea instance scan <instance>` | Die Mod-Ordner ausgeben, die Borea nicht installiert hat, und die erfassten Mods, deren Ordner fehlt. |
| `borea instance adopt <instance> <folder> --archive <path>` | Einen Mod-Ordner, den Borea nicht installiert hat, als das Index-Release erfassen, zu dem sein Archiv passt. |
| `borea instance take-ownership <instance> <mod-id> [--with-dependencies] [--without-dependencies]` | Das Release, als das ein Mod erfasst ist, über seinen Ordner installieren, damit Borea seine Dateien verwaltet und ihn aktualisieren und entfernen kann. Dateien im Ordner, die das Release nicht enthält, gehen verloren. Eine fehlende Pflichtabhängigkeit bricht den Befehl ab, sofern `--with-dependencies` sie nicht vorher installiert oder `--without-dependencies` ohne sie weitermacht. |
| `borea instance import-profile <name> [--dry-run]` | Eine Instanz aus Kopien der Mods im gemeinsamen Profil anlegen, mit derselben Ladereihenfolge und denselben aktiven Mods. Das gemeinsame Profil bleibt, wie es ist. |
| `borea instance rename-save <instance> <save> <new-name>` | Einem Spielstand einen neuen Namen geben. Borea schreibt ihn in die `meta.toml` des Spielstands und benennt dessen Ordner passend um. Das Spiel akzeptiert Buchstaben, Ziffern, einzelne Leerzeichen, `-` und `_`, höchstens 64 Zeichen. Schließe vorher das Spiel. |
| `borea instance rename-vehicle <instance> <vehicle> <new-name>` | Einem gespeicherten Fahrzeug auf dieselbe Weise einen neuen Namen geben. |
| `borea instance backups <instance>` | Die Backups der Spielstände und Fahrzeuge einer Instanz ausgeben, neueste zuerst, mit Zeitpunkt, Grund und Größe. |
| `borea instance restore-backup <instance> <backup> [--replace]` | Ein Backup dorthin zurücklegen, wo es herkam. `--replace` verschiebt einen gleichnamigen Spielstand oder ein gleichnamiges Fahrzeug vorher in die Backups. Schließe vorher das Spiel. |
| `borea instance delete-backup <instance> <backup>` | Ein Backup endgültig löschen. |
| `borea enable <mod-id> [--instance <instance>]` | Das Spiel einen Mod laden lassen. |
| `borea disable <mod-id> [--instance <instance>]` | Verhindern, dass das Spiel einen Mod lädt. |
| `borea pin <mod-id> [--instance <instance>]` | Einen Mod auf seiner Version halten. Updates und Pack-Updates lassen ihn in Ruhe, und ein Plan, der eine andere Version von ihm braucht, bricht ab. |
| `borea unpin <mod-id> [--instance <instance>]` | Updates wieder erlauben, die Version eines fixierten Mods zu ändern. |

## Aufbau des Repositories

| Pfad | Beschreibung |
| --- | --- |
| `src/Borea.Core` | Das Domänenmodell: Mods, Packs, Instanzen, Planung, die Schnittstellen, die die anderen Projekte implementieren. Kein I/O. |
| `src/Borea.Storage` | Alles auf der Festplatte: Einstellungen, Instanzen, Backups, TOML, der Index-Cache. |
| `src/Borea.Network` | Alles über das Netzwerk: der Content-Index, SpaceDock, Downloads, Bilder. |
| `src/Borea.Composition` | Die Composition Root. Baut jeden Dienst einmal aus den gespeicherten Einstellungen, für die App und die CLI. |
| `src/Borea.App` | Die Desktop-App, gebaut mit Avalonia. |
| `src/Borea.Cli` | Die Kommandozeile, `borea`. Eine dünne Schicht über denselben Diensten. |
| `tests` | Ein Testprojekt pro Quellprojekt. |

## Bauen

Borea braucht .NET 10. Mit installiertem SDK:

```sh
dotnet build
dotnet test
dotnet run --project src/Borea.App
```

## Mithelfen

Lies erst [CONTRIBUTING.md](CONTRIBUTING.md). Fehler gehören in den Issue-Tracker, größere Ideen in die [content-manager-design Discussions](https://github.com/KSAModding/content-manager-design/discussions).

Borea wird auf [Hosted Weblate](https://hosted.weblate.org/engage/borea/) übersetzt, im Browser und ohne git oder C#. Der [Leitfaden zur Lokalisierung](docs/localization.md) erklärt, wie das geht und wie du stattdessen auf der GitHub-Website einen Text korrigierst oder eine neue Sprache vorschlägst.

## Danke an

- [MrJeranimo](https://github.com/MrJeranimo) hat Borea ins Leben gerufen, ist einer der Maintainer und verantwortet Architektur und Ausrichtung.
- [Maximilian-Nesslauer](https://github.com/Maximilian-Nesslauer), der Content-Index und seine RFCs sowie der größte Teil der App und der CLI in ihrer heutigen Form.
- [averageksp](https://github.com/averageksp), App-Funktionen, Tests und die Listings im Content-Index.
- [PlazmaBoltz](https://github.com/PlazmaBoltz), die erste Oberfläche und die Designs.
- [renancamm](https://github.com/renancamm) (beik), UI/UX-Arbeit an der App-Oberfläche und das Borea-Symbol.

Und an alle, die einen Fehler gemeldet, einen Build getestet oder einen Mod gelistet haben.

## Lizenz

Borea steht unter der [MIT-Lizenz](LICENSE).
`THIRD-PARTY-NOTICES.txt` in jedem Release listet die Lizenzen der mitgelieferten Software.

Das Borea-Logo fällt nicht unter die MIT-Lizenz. Siehe [BRANDING.md](BRANDING.md).
