# UVR Audio Track Extractor V5 | Ricomposizione 5.1 offline

Applicazione desktop per Windows 10/11, in italiano, basata su Windows Forms e FFmpeg.
**Non richiede Internet** durante l'utilizzo. La cartella `dist\bin` deve contenere
`ffmpeg.exe` e `ffprobe.exe` oppure i due programmi devono essere nel PATH.

## Novita' V5: RICOMPONI 5.1

Nella schermata principale e' stato aggiunto il pulsante **RICOMPONI 5.1**.
Si apre una seconda finestra che permette di ricostruire una traccia surround a partire da:

- 6 WAV mono completi: **FL, FR, FC, LFE, SL, SR**;
- 6 WAV stereo prodotti da UVR: ogni ingresso viene convertito in mono;
- 6 cartelle con chunk WAV gia' elaborati in UVR (numerati `_chunk_0000`, `_chunk_0001`, ecc.);
- una combinazione di WAV completi e cartelle chunk, purche' le durate corrispondano;
- facoltativamente, un WAV o una cartella chunk **VOCE ITA**, miscelata nel canale centrale.

Per i canali posteriori BL/BR puoi selezionare **5.1(back)** invece di **5.1(side)**.
Non confondere i due layout: devi scegliere quello corrispondente alle sorgenti.

### Utilizzo

1. Avvia `dist\UVR_Audio_Track_Extractor.exe`.
2. Premi **RICOMPONI 5.1**.
3. Per ciascuno dei sei canali scegli **File WAV...** oppure **Chunk...**.
   Il pulsante **Riconosci automaticamente** tenta di compilare i sei percorsi
   da una cartella principale. Se trova piu' serie possibili non indovina:
   scegli i canali manualmente.
4. Facoltativo: scegli la sorgente **VOCE ITA** per inserirla nel centrale.
   Il controllo **FC originale** regola il volume del centrale (1,00 = invariato).
   Lo **slider Voce italiana** regola il doppiaggio da -12 a +6 dB e parte
   da **+6 dB**. Lo slider agisce soltanto se hai scelto VOCE ITA;
   non altera direttamente un FLAC 5.1 gia' prodotto. Nel mix viene applicato
   un limiter al centrale per contenere i picchi.
5. Premi **Salva come...** e scegli:
   - **FLAC 5.1**: lossless, 24 bit, 48 kHz, consigliato come master;
   - **WAV 5.1**: PCM 24 bit, 48 kHz, file molto grande;
   - **AC3 5.1**: 640 kbps, 48 kHz, per lettori e video.
6. Premi **RICOMPONI AUDIO 5.1**. Segui la barra di avanzamento.
7. Il file viene pubblicato solo al termine, mai parzialmente.

### Chunk UVR

Se scegli una cartella, essa deve contenere **una sola serie** di WAV elaborati,
ad esempio:

    ENG_FL_chunk_0000_(Instrumental).wav
    ENG_FL_chunk_0001_(Instrumental).wav
    ENG_FL_chunk_0002_(Instrumental).wav

I numeri devono essere consecutivi, senza duplicati.
Se UVR crea `Vocals` e `Instrumental` insieme nella stessa cartella,
**sposta i file della serie desiderata in una cartella dedicata**.
L'app non deve decidere automaticamente quale elaborazione sia quella corretta.

La ricomposizione verifica:
- presenza di tutti i sei canali;
- WAV mono/stereo e durata rilevabile;
- stessa durata totale dei sei canali (scarto massimo 0,25 s);
- se FC e l'altro canale sono entrambi a chunk, stesso numero di chunk
  e differenza massima 0,10 s per ogni chunk;
- la voce opzionale deve essere sincronizzata con FC (scarto massimo 0,5 s).

**Importante:** i controlli sulle durate non garantiscono da soli che UVR non abbia
introdotto un ritardo *all'interno* del segnale. In caso di disallineamento
dovrai correggere il timing prima della ricomposizione. I chunk vengono
concatenati senza crossfade, per non duplicare campioni e non spostare i dialoghi.

### Mix voce italiana

La casella VOCE ITA e' **facoltativa**. Senza di essa, i sei canali vengono
ricomposti senza mix, normalizzazione o modifiche ai livelli (eccetto l'eventuale
downmix mono di WAV stereo in ingresso e la conversione a 48 kHz).

Con VOCE ITA, il programma:
- riduce/aumenta il centrale secondo il valore **FC originale**;
- riduce/aumenta la voce secondo lo slider **Voce italiana** (-12 / +6 dB,
  valore iniziale +6 dB, pari a circa 1,995 volte l'ampiezza);
- miscela i due segnali nel solo FC e limita i picchi;
- conserva FL, FR, LFE, SL, SR separati.

Se hai gia' miscelato il doppiaggio nel WAV FC, **non selezionare VOCE ITA**,
altrimenti sentirai la voce due volte.

### Annullamento sicuro

L'elaborazione usa una cartella temporanea `.UVR_RICOMPONE_TEMP_*`
nella destinazione. Quando premi **Annulla**:
- FFmpeg/FFprobe vengono interrotti;
- i WAV, i FLAC, gli AC3 e gli elenchi temporanei creati da questa operazione
  vengono eliminati;
- gli ingressi e i file preesistenti restano intatti.

Se il file finale esiste gia', la V5 rifiuta di sovrascriverlo.
Se Windows o un antivirus impedisce la pulizia, compare un errore e il percorso
dei temporanei rimasti. Un arresto forzato del PC non garantisce la pulizia.

### Limiti attuali

- La V5 ricompone **l'audio 5.1**, non lo reinserisce automaticamente in un MKV.
- Non applica un riallineamento automatico intelligente dei dialoghi.
- La conversione AC3 aggiunge il normale arrotondamento alla durata dei frame
  AC3 (32 ms); per il master di sincronizzazione preferisci FLAC.
- I modelli UVR possono introdurre artefatti ai bordi dei chunk. Se possibile,
  elabora i WAV interi oppure usa la gestione interna dei segmenti di UVR.

## Estrazione V4 mantenuta

Tutte le funzioni precedenti restano disponibili:
- scelta delle tracce audio del video;
- esportazione WAV multicanale;
- separazione in FL/FR/FC/LFE/SL/SR;
- downmix mono di tracce stereo/5.1/7.1;
- chunk da 5 a 600 secondi;
- salvataggio WAV completi opzionale;
- annullamento con eliminazione dei temporanei.

**V5:** "Dividi in chunk per UVR" e' ora **disattivato di default**,
perche' UVR puo' gestire internamente i segmenti. Puoi riattivarlo quando serve.

## Compilazione offline su Windows

1. Estrai lo ZIP in una cartella locale.
2. Esegui `COMPILA_EXE.bat`.
3. Il compilatore .NET Framework 4.x di Windows compila
   `UVR_Audio_Track_Extractor.cs` + `RecomposeForm.cs`.
4. Avvia `dist\UVR_Audio_Track_Extractor.exe`.

Se `ffmpeg.exe` e `ffprobe.exe` sono gia' nel PATH, il batch tenta di copiarli
in `dist\bin`. In caso contrario copiali manualmente in `dist\bin`.
Per spostare l'app su un altro computer offline, copia tutta la cartella `dist`.

**Verifica effettuata:** filtri FFmpeg provati con audio sintetico mono e stereo,
chunk concatenati, mix voce, FLAC/WAV/AC3, layout 5.1(side)/5.1(back).
**La compilazione Windows Forms non e' stata eseguita in questo ambiente**:
se `COMPILA_EXE.bat` segnala un errore, invia il testo della console.
