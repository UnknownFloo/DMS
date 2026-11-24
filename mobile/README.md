# Paperless Mobile (Expo Go)

Mobile companion app for the Paperless DMS API. It lists and searches documents, opens OCR text and saved AI summaries, and uploads PDFs selected from the phone.

## Run it on a phone

1. Start the backend from the project root with `docker compose up --build`.
2. Connect the phone and computer to the same Wi-Fi network.
3. Find the computer's LAN IP (`ipconfig` on Windows, `hostname -I` on Linux, or Network settings on macOS).
4. In this folder run `npm install`, then `npx expo install --fix`, then `npx expo start --clear --tunnel` (clears the old Metro cache after the SDK/component update).
5. Install the Expo Go build for SDK 58 on the phone and scan the QR code. Open **Verbindung** in the app and set the API address to `http://<COMPUTER-IP>:8080/api`, for example `http://192.168.1.20:8080/api`. The app tests `/health` before saving the address.

The app does not assume a default API address. Docker exposes the API on port 8080; allow that port through the computer's firewall on the private network. `--tunnel` carries the Expo development bundle to the phone; the API itself must still be reachable from the phone over Wi-Fi. For a local Android emulator, use `http://10.0.2.2:8080/api`.

Open a document and tap **PDF anzeigen** to download it and open the phone's PDF preview. Upload uses Expo SDK 58's native file picker and multipart upload so Android can keep the document provider's read permission. The app uses `react-native-safe-area-context` for device insets.
