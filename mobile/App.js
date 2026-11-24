import React, { useCallback, useEffect, useState } from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  ActivityIndicator, Alert, FlatList, KeyboardAvoidingView, Platform,
  Pressable, RefreshControl, ScrollView, StatusBar,
  StyleSheet, Text, TextInput, View,
} from 'react-native';
import { SafeAreaProvider, SafeAreaView } from 'react-native-safe-area-context';
import { File, Paths, UploadType } from 'expo-file-system';

const DEFAULT_API = '';
const colors = { ink: '#1f2d1f', muted: '#70806d', green: '#3d6f45', pale: '#edf7ea', bg: '#f5f1e8', line: '#dfe5da', white: '#fff' };

export default function App() {
  const [apiUrl, setApiUrl] = useState(DEFAULT_API);
  const [draftUrl, setDraftUrl] = useState(DEFAULT_API);
  const [tab, setTab] = useState('documents');
  const [documents, setDocuments] = useState([]);
  const [selected, setSelected] = useState(null);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [openingPdf, setOpeningPdf] = useState(false);
  const [checkingConnection, setCheckingConnection] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    AsyncStorage.getItem('paperless-api-url').then(saved => {
      if (saved) { setApiUrl(saved); setDraftUrl(saved); }
    }).catch(() => {});
  }, []);

  const request = useCallback(async (path, options = {}) => {
    const base = apiUrl.trim().replace(/\/$/, '');
    if (!base) throw new Error('Bitte zuerst unter „Verbindung“ die API-Adresse deines Computers eintragen.');
    if (!/^https?:\/\//i.test(base)) throw new Error('API-Adresse muss mit http:// oder https:// beginnen.');
    const response = await fetch(`${base}${path}`, options);
    if (!response.ok) {
      const body = await response.text();
      throw new Error(body || `Serverfehler ${response.status}`);
    }
    return response.status === 204 ? null : response.json();
  }, [apiUrl]);

  const loadDocuments = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const data = await request(`/documents${search.trim() ? `/search?q=${encodeURIComponent(search.trim())}` : ''}`);
      setDocuments(data || []);
    } catch (e) { setError(`Server nicht erreichbar: ${e.message}`); }
    finally { setLoading(false); }
  }, [request, search]);

  useEffect(() => { if (tab === 'documents') loadDocuments(); }, [tab, loadDocuments]);

  const openDocument = async item => {
    try { setSelected(await request(`/documents/${item.id}`)); setTab('detail'); }
    catch (e) { Alert.alert('Dokument konnte nicht geladen werden', e.message); }
  };

  const uploadPdf = async () => {
    try {
      const result = await File.pickFileAsync({ mimeTypes: ['application/pdf'] });
      if (result.canceled) return;
      const file = result.result;
      if (!file?.uri) throw new Error('Der PDF-Dateipfad konnte nicht gelesen werden.');
      setUploading(true);
      const base = apiUrl.trim().replace(/\/$/, '');
      if (!base) throw new Error('Bitte zuerst unter „Verbindung“ die API-Adresse deines Computers eintragen.');
      const response = await file.upload(`${base}/documents/upload`, {
        httpMethod: 'POST',
        uploadType: UploadType.MULTIPART,
        fieldName: 'file',
        mimeType: 'application/pdf',
      });
      if (response.status < 200 || response.status >= 300) {
        throw new Error(response.body || `Serverfehler ${response.status}`);
      }
      Alert.alert('Upload gestartet', 'Das PDF wird im Hintergrund erkannt und verarbeitet.');
      setTab('documents');
    } catch (e) {
      const reason = e.message === 'Network request failed'
        ? `Server nicht erreichbar (${apiUrl || 'keine API-Adresse'}). Prüfe die Computer-IP, Docker Compose und ob Handy und PC im selben WLAN sind.`
        : e.message;
      Alert.alert('Upload fehlgeschlagen', reason);
    }
    finally { setUploading(false); }
  };

  const openPdf = async () => {
    if (!selected?.id || !apiUrl) {
      Alert.alert('PDF nicht verfügbar', 'Verbinde zuerst die App mit dem Paperless-Server.');
      return;
    }
    setOpeningPdf(true);
    try {
      const base = apiUrl.trim().replace(/\/$/, '');
      const safeName = (selected.fileName || `paperless-${selected.id}.pdf`).replace(/[^a-zA-Z0-9._-]/g, '_');
      const localFile = await File.downloadFileAsync(
        `${base}/documents/${selected.id}/file`,
        new File(Paths.cache, `${Date.now()}-${safeName}`),
      );
      if (!(await File.canPreview(localFile, { mimeType: 'application/pdf' }))) {
        throw new Error('Auf diesem Gerät ist keine PDF-Vorschau verfügbar.');
      }
      await File.preview(localFile, { title: selected.fileName || 'Paperless PDF', mimeType: 'application/pdf' });
    } catch (e) {
      Alert.alert('PDF konnte nicht geöffnet werden', e.message === 'Network request failed'
        ? 'Server nicht erreichbar. Prüfe Verbindung und Computer-IP.'
        : e.message);
    } finally { setOpeningPdf(false); }
  };

  const saveServer = async () => {
    let value = draftUrl.trim().replace(/\/$/, '');
    if (!/^https?:\/\//i.test(value)) {
      Alert.alert('Adresse prüfen', 'Trage eine vollständige Adresse ein, z. B. http://192.168.1.20:8080/api');
      return;
    }
    if (!/\/api$/i.test(value)) value += '/api';
    if (/^https?:\/\/(localhost|127\.0\.0\.1)(:|\/|$)/i.test(value)) {
      Alert.alert('Computer-IP verwenden', 'Auf dem Handy zeigt localhost auf das Handy selbst. Verwende die WLAN-IP deines Computers, z. B. http://192.168.1.20:8080/api.');
      return;
    }
    setCheckingConnection(true);
    try {
      const server = value.replace(/\/api$/i, '');
      const response = await fetch(`${server}/health`);
      if (!response.ok) throw new Error(`Health-Check antwortet mit ${response.status}.`);
      await AsyncStorage.setItem('paperless-api-url', value);
      setApiUrl(value); setDraftUrl(value); setSelected(null); setTab('documents');
      Alert.alert('Verbunden', 'Paperless-API ist erreichbar.');
    } catch (e) {
      Alert.alert('Verbindung fehlgeschlagen', `Konnte ${value.replace(/\/api$/i, '')}/health nicht erreichen. Prüfe IP-Adresse, Docker Compose und WLAN. ${e.message || ''}`);
    } finally { setCheckingConnection(false); }
  };

  const header = <View style={styles.top}>
    <View style={styles.brandRow}><View style={styles.logo}><Text style={styles.logoText}>P</Text></View><View><Text style={styles.eyebrow}>DOCUMENT MANAGEMENT</Text><Text style={styles.brand}>Paperless</Text></View></View>
    <Text style={styles.tagline}>Dokumente. OCR. KI-Zusammenfassungen.</Text>
  </View>;

  const nav = <View style={styles.nav}>
    {[['documents', 'Dokumente'], ['upload', 'Hochladen'], ['settings', 'Verbindung']].map(([key, label]) => <Pressable key={key} onPress={() => { setTab(key); setSelected(null); }} style={[styles.navItem, tab === key || (key === 'documents' && tab === 'detail') ? styles.navActive : null]}><Text style={[styles.navText, tab === key || (key === 'documents' && tab === 'detail') ? styles.navTextActive : null]}>{label}</Text></Pressable>)}
  </View>;

  return <SafeAreaProvider><SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.safe}>
    <StatusBar barStyle="dark-content" backgroundColor={colors.bg} />
    {header}{nav}
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      {tab === 'documents' && <View style={styles.flex}>
        <View style={styles.heading}><Text style={styles.title}>Deine Dokumente</Text><Text style={styles.sub}>{documents.length} gespeichert</Text></View>
        <TextInput value={search} onChangeText={setSearch} placeholder="Dokumente durchsuchen" placeholderTextColor={colors.muted} style={styles.search} returnKeyType="search" onSubmitEditing={loadDocuments} />
        {error ? <Pressable onPress={loadDocuments} style={styles.errorBox}><Text style={styles.errorText}>{error}{'\n'}Tippen zum erneuten Verbinden.</Text></Pressable> : null}
        {loading && documents.length === 0 ? <ActivityIndicator color={colors.green} style={{ marginTop: 32 }} /> : <FlatList data={documents} keyExtractor={item => item.id} contentContainerStyle={styles.list} refreshControl={<RefreshControl refreshing={loading} onRefresh={loadDocuments} tintColor={colors.green} />} ListEmptyComponent={<Text style={styles.empty}>Noch keine Dokumente gefunden.</Text>} renderItem={({ item }) => <Pressable onPress={() => openDocument(item)} style={styles.card}>
          <View style={styles.pdfIcon}><Text style={styles.pdfText}>PDF</Text></View><View style={styles.cardBody}><Text style={styles.cardTitle} numberOfLines={2}>{item.fileName}</Text><Text style={styles.cardMeta}>{item.description || 'PDF-Dokument'}{item.summary ? '  ·  KI-Zusammenfassung' : ''}</Text></View><Text style={styles.chevron}>›</Text>
        </Pressable>} />}
      </View>}

      {tab === 'detail' && <ScrollView contentContainerStyle={styles.detail}>
        <Pressable onPress={() => setTab('documents')}><Text style={styles.back}>‹  Zurück zu Dokumenten</Text></Pressable>
        <Text style={styles.title}>{selected?.fileName}</Text>
        <Text style={styles.sub}>Hochgeladen {selected?.uploadedAtUtc ? new Date(selected.uploadedAtUtc).toLocaleDateString() : ''}</Text>
        <Pressable onPress={openPdf} disabled={openingPdf} style={[styles.primaryButton, openingPdf && styles.disabled]}>{openingPdf ? <ActivityIndicator color={colors.white} /> : <Text style={styles.buttonText}>PDF anzeigen</Text>}</Pressable>
        {selected?.description ? <View style={styles.section}><Text style={styles.sectionTitle}>Beschreibung</Text><Text style={styles.bodyText}>{selected.description}</Text></View> : null}
        <View style={styles.section}><Text style={styles.sectionTitle}>KI-Zusammenfassung</Text><Text style={styles.bodyText}>{selected?.summary || 'Noch keine Zusammenfassung verfügbar. Bitte warte, bis die Verarbeitung abgeschlossen ist.'}</Text></View>
        <View style={styles.section}><Text style={styles.sectionTitle}>Erkannter Text</Text><Text style={styles.ocrText}>{selected?.ocrText || 'OCR wird verarbeitet …'}</Text></View>
      </ScrollView>}

      {tab === 'upload' && <ScrollView contentContainerStyle={styles.uploadPage}>
        <Text style={styles.title}>PDF vom Handy</Text><Text style={styles.sub}>Wähle ein PDF aus deinen Dateien. OCR startet automatisch.</Text>
        <Pressable onPress={uploadPdf} disabled={uploading} style={[styles.primaryButton, uploading && styles.disabled]}>{uploading ? <ActivityIndicator color={colors.white} /> : <Text style={styles.buttonText}>PDF auswählen und hochladen</Text>}</Pressable>
        <View style={styles.tip}><Text style={styles.sectionTitle}>Nach dem Upload</Text><Text style={styles.bodyText}>Das Dokument erscheint in deiner Liste. Die Texterkennung und KI-Zusammenfassung laufen im Hintergrund.</Text></View>
      </ScrollView>}

      {tab === 'settings' && <ScrollView contentContainerStyle={styles.uploadPage}>
        <Text style={styles.title}>Server verbinden</Text><Text style={styles.sub}>Gib die IP-Adresse deines Computers im selben WLAN ein.</Text>
        <Text style={styles.label}>API-Adresse</Text><TextInput autoCapitalize="none" autoCorrect={false} keyboardType="url" value={draftUrl} onChangeText={setDraftUrl} style={styles.input} placeholder="http://192.168.1.20:8080/api" />
        <Pressable onPress={saveServer} disabled={checkingConnection} style={[styles.primaryButton, checkingConnection && styles.disabled]}>{checkingConnection ? <ActivityIndicator color={colors.white} /> : <Text style={styles.buttonText}>Verbindung prüfen und speichern</Text>}</Pressable>
        <View style={styles.tip}><Text style={styles.sectionTitle}>Wo finde ich die IP?</Text><Text style={styles.bodyText}>Am Computer: Windows mit ipconfig, macOS/Linux mit hostname -I. Handy und Computer müssen im selben WLAN sein. Docker Compose muss laufen.</Text></View>
        <Text style={styles.current}>Aktuell: {apiUrl}</Text>
      </ScrollView>}
    </KeyboardAvoidingView>
  </SafeAreaView></SafeAreaProvider>;
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.bg }, flex: { flex: 1 },
  top: { paddingHorizontal: 20, paddingTop: 10, paddingBottom: 15 },
  brandRow: { flexDirection: 'row', alignItems: 'center', gap: 11 }, logo: { width: 42, height: 42, borderRadius: 13, backgroundColor: colors.green, alignItems: 'center', justifyContent: 'center' }, logoText: { color: colors.white, fontWeight: '800', fontSize: 23 }, eyebrow: { color: colors.muted, fontSize: 9, letterSpacing: 1.2, fontWeight: '700' }, brand: { color: colors.ink, fontSize: 21, fontWeight: '800' }, tagline: { color: colors.muted, marginTop: 11, fontSize: 13 },
  nav: { flexDirection: 'row', backgroundColor: colors.white, borderTopWidth: 1, borderBottomWidth: 1, borderColor: colors.line, paddingHorizontal: 12 }, navItem: { paddingVertical: 13, paddingHorizontal: 12, borderBottomWidth: 3, borderBottomColor: 'transparent', marginRight: 9 }, navActive: { borderBottomColor: colors.green }, navText: { color: colors.muted, fontWeight: '600', fontSize: 13 }, navTextActive: { color: colors.green },
  heading: { paddingHorizontal: 20, paddingTop: 23, paddingBottom: 13 }, title: { color: colors.ink, fontSize: 25, fontWeight: '800' }, sub: { color: colors.muted, marginTop: 5, fontSize: 13 }, search: { marginHorizontal: 20, marginBottom: 9, padding: 13, borderRadius: 12, backgroundColor: colors.white, borderWidth: 1, borderColor: colors.line, color: colors.ink }, list: { paddingHorizontal: 16, paddingBottom: 30 }, card: { flexDirection: 'row', alignItems: 'center', backgroundColor: colors.white, borderRadius: 15, borderWidth: 1, borderColor: colors.line, padding: 14, marginVertical: 6, gap: 12 }, pdfIcon: { width: 42, height: 48, borderRadius: 10, backgroundColor: '#fff0eb', alignItems: 'center', justifyContent: 'center' }, pdfText: { fontSize: 11, fontWeight: '800', color: '#a04b32' }, cardBody: { flex: 1 }, cardTitle: { fontSize: 15, fontWeight: '700', color: colors.ink }, cardMeta: { fontSize: 12, color: colors.muted, marginTop: 5 }, chevron: { color: colors.muted, fontSize: 25 }, empty: { textAlign: 'center', color: colors.muted, marginTop: 36 },
  detail: { padding: 20, paddingBottom: 40 }, back: { color: colors.green, fontWeight: '700', marginBottom: 22 }, section: { marginTop: 22, padding: 16, backgroundColor: colors.white, borderRadius: 14, borderWidth: 1, borderColor: colors.line }, sectionTitle: { color: colors.ink, fontWeight: '800', fontSize: 15, marginBottom: 9 }, bodyText: { color: colors.ink, lineHeight: 22, fontSize: 14 }, ocrText: { color: colors.muted, lineHeight: 21, fontSize: 13 },
  uploadPage: { padding: 20, paddingTop: 25 }, primaryButton: { marginTop: 22, borderRadius: 13, padding: 16, backgroundColor: colors.green, alignItems: 'center', justifyContent: 'center', minHeight: 54 }, buttonText: { color: colors.white, fontSize: 15, fontWeight: '800' }, disabled: { opacity: 0.6 }, tip: { marginTop: 24, padding: 16, borderRadius: 14, backgroundColor: colors.pale }, label: { marginTop: 25, marginBottom: 7, fontSize: 13, fontWeight: '700', color: colors.ink }, input: { padding: 14, borderRadius: 12, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.white, color: colors.ink }, current: { marginTop: 20, color: colors.muted, fontSize: 12 }, errorBox: { marginHorizontal: 20, marginVertical: 8, padding: 12, backgroundColor: '#fdecea', borderRadius: 10 }, errorText: { color: '#8e3028', fontSize: 13 },
});
