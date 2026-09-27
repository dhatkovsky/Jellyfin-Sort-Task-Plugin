# Jellyfin Sort Title Updater Plugin

A custom, high-performance plugin for **Jellyfin 12** built on **.NET 10**. This plugin introduces a scheduled library task that forces a converted UTF-16 binary representation into the `SortTitle` metadata of your media. 

It is designed to solve complex multi-language (Cyrillic, Mandarin, Japanese and many more) sorting issues by guaranteeing a strict binary character-by-character ordering across your entire library.
There might be sorting issues for languages using some alphabets like Belarusian, Serbian, Ukrainian for Cyrillic or Polish for Latin alphabet, so if issue remains, please, feel free to fork or post a request with details what to update.
Sorting of Seasons is switched off so that Season 10 will not come before Season 2.
Please, ensure you use .nfo files in the configuration of your libraries.

## How the Algorithm Works
The plugin takes the original `Title` (Name) of any media asset and encodes every character using its 16-bit **UTF-16** hex code:
1. It breaks the character down into 4 hexadecimal digits (nibbles).
2. Each hex digit (value `0-15` / `0-F`) is shifted by adding it to the ASCII value of the lowercase character `'a'`.
3. The resulting string uses only letters from `'a'` to `'p'`, separated by a hyphen `-` after each encoded character.

*Example:* A regular space character (`0x0020`) becomes encoded as **`aaca-`**. 

## Features
* **Full Media Support:** Processes Movies, Series, Music Artists, Albums, Audio Tracks, Books, and BoxSets.
* **Granular Control:** Includes a native-designed configuration page where you can check/uncheck specific server libraries (Virtual Folders).
* **Deep Sync Integration:** Bypasses standard core file-system locks (`ForcedSortName`), writing the generated `<sorttitle>` directly into local `.nfo` files and syncing metadata seamlessly with the SQLite database.
* **Native Look & Feel:** Configuration dashboard aligns perfectly with any active Jellyfin web-client design theme and includes success toast alerts.

## Installation

### Via Custom Repository (Recommended)
1. Navigate to your Jellyfin server **Dashboard -> Plugins -> Repositories**.
2. Click **Add** and paste your custom manifest URL:
   `https://github.com/dhatkovsky/Jellyfin-Sort-Task-Plugin/releases/download/v1.0.1/manifest.json`
3. Go to the **Catalog** tab, find **Sort Title Updater**, and click install.
4. Restart your Jellyfin server.

### Manual Installation
1. Download the compiled `Jellyfin.Plugin.SortTitleUpdater.dll`.
2. Move the `.dll` file to your server plugins directory: `plugins/SortTitleUpdater/SortTitleUpdater.dll`.
3. Restart your Jellyfin server.

## Usage
1. Go to **Dashboard -> Plugins** and click on **Sort Title Updater**.
2. Select the libraries you want to process and click **Save Settings**.
3. Navigate to **Dashboard -> Scheduled Tasks**.
4. Run the **Update Library Sort Titles** task manually or let it trigger automatically according to your preferred schedule (defaults to daily at 2:00 AM).

## License
This project is licensed under the MIT License.

## Credits
This project was created by impressive free Google AI