# LPC54628 Digital Loopback Design Memo

## Formål

Den digitale loopback fra denne kodebase bør løftes over i LPC54628-projektet som en intern referencefunktion for digital audio-transport.

Formålet er ikke at genbruge MCXN947-platformlaget, men at genbruge den måde systemet:

- validerer audio-pakker
- styrer stream state
- måler bufferhelbred
- detekterer discontinuities
- bruges som reference for USB performance og daglig verifikation

Digital loopback skal betragtes som en digital referencevej.

Den skal bruges til:

- USB-protokoltest
- latency- og jittermåling
- payload-integritetscheck
- regressionskørsel efter firmwareændringer
- daglig verifikation af den digitale vej

Den skal ikke bruges som dokumentation for analog audio-performance.

## Hvad Der Skal Genbruges

Det vigtigste der skal løftes over er den platform-neutrale logik.

### Audio header og stream-kontrakt

Genbrug audio-headeren og de tilhørende streamregler fra:

- [services/audio_stream_format.h](services/audio_stream_format.h)
- [services/audio_stream_service.h](services/audio_stream_service.h)
- [services/audio_stream_service.cpp](services/audio_stream_service.cpp)

Det giver en lille og tydelig digital kontrakt:

- stream skal være startet før audio accepteres
- hver packet er selvbeskrivende
- aktivt format håndhæves
- sekvensnumre overvåges
- fejl bliver synlige og målbare

### Playback buffer og metrics

Genbrug ringbuffer-mønsteret fra:

- [services/audio_playback_buffer.h](services/audio_playback_buffer.h)
- [services/audio_playback_buffer.cpp](services/audio_playback_buffer.cpp)

Det vigtige er ikke bare buffering, men metrics:

- fill level
- min og max fill
- underrun count
- overrun count
- dropped bytes
- start threshold

### Periodisk drain-model

Genbrug idéen fra:

- [audio_tx_service.cpp](audio_tx_service.cpp)

Den giver en enkel måde at simulere eller modellere device-side forbrug ved den rigtige audio-rate, så buffer-målinger bliver meningsfulde selv før den endelige playback-kæde er færdig.

## Hvad Der Ikke Skal Genbruges Direkte

De følgende dele skal ikke kopieres direkte ind i LPC54628-kodebasen:

- [usb_hw_init.c](usb_hw_init.c)
- [usb_vendor_bulk.c](usb_vendor_bulk.c)
- [frdmmcxn947_cm33_core0/board_files.cmake](frdmmcxn947_cm33_core0/board_files.cmake)
- board-, clock-, pinmux- og hardware-init under [frdmmcxn947_cm33_core0](frdmmcxn947_cm33_core0)

Årsagen er, at de er bundet til MCXN947 board, clock tree, PHY og USB-driverintegration.

LPC54628 skal have sine egne platform-adaptere.

## Anbefalet Integration I LPC54628

LPC54628-projektet har allerede sin egen kommando/event-protokol. Den skal bevares.

Derfor bør den digitale loopback integreres som et internt servicelag bag den eksisterende protokol.

Anbefalet lagdeling:

1. eksisterende LPC kommando/event-protokol
2. audio test controller
3. stream- og buffer-services
4. LPC54628 USB-transportadapter

Det betyder:

- ingen protokolmigrering
- genbrug af eksisterende host- og device-kontrakter
- ny funktionalitet implementeres internt bag kendte kommandoer

## Implementeringsrækkefølge: Digital Loopback Før Hardware

**Vigtig sekvensering:** Den digitale loopback skal implementeres og valideres **før** codec, I2S, DMA og analog audio-streaming bringes ind.

### Fase 1: Digital Loopback Reference (ingen hardware)

1. USB enumeration stabil
2. Platform-neutrale services: audio header, stream service, playback buffer
3. USB round-trip: device modtager og gendanner audio packets
4. Metrics: stream health og buffer status
5. Negative tests: bad header, format mismatch, sequence gaps

**Output fra fase 1:** Valideret USB-protocol, kendt-godt referencepunkt

### Fase 2: Hardware Integration (codec, I2S, DMA)

Når digital loopback er stabil kan hardware tilføjes:

1. I2S og SAI-konfiguration
2. Codec initialisering og kontrol
3. DMA audio-path
4. Analog input/output

**Fordele ved denne rækkefølge:**

- USB-lagene er isoleret og valideret
- Hvis der senere bliver problemer ved hardware-tilføjelse, ved du de er codec/I2S-relateret, ikke USB
- Du har en kendt-god reference for senere debugging
- Mindre risiko for at problemer opstår fra multiple kilder samtidig

### Fase 3: Kapacitetsbygning

Efter hardware-vej virker:

1. Generator-moder
2. Mikrofon capture
3. Codec-input til USB
4. Analog verifikation

## Anbefalet Intern API-retning

Den nye kodebase bør have en lille intern API med tre ansvar.

### 1. Loopback service

Ansvar:

- start og stop stream
- validere indkommende audio packet
- håndhæve aktivt format
- detektere sekvenshop
- bygge loopback-respons
- eksponere metrics

### 2. Playback buffer

Ansvar:

- modtage payload-data
- læse ud i hele frames
- tracke bufferhelbred

### 3. USB transportadapter for LPC54628

Ansvar:

- modtage USB audio OUT packet
- kalde loopback-servicen
- sende USB audio IN packet
- melde transport ready og reset videre til service-laget

Platform-neutral kode må ikke kende LPC SDK eller endpointdetaljer.

## Første Implementeringstrin

Første implementering i LPC54628 bør være snæver.

Start med kun:

- digital loopback reference mode
- eksisterende kommando/event-protokol
- audio header og streamvalidering
- playback buffer
- USB round-trip af audio packets
- device metrics

Vent med:

- generator mode
- mikrofon capture
- codec capture
- analog verifikation

Det giver den korteste vej til en stabil referencefunktion.

## Minimum Metrics

Device bør mindst kunne rapportere:

- stream active
- playback fill level
- playback min fill level
- playback max fill level
- playback underrun count
- playback overrun count
- playback dropped bytes
- discontinuity count
- invalid header count
- format mismatch count

Host-værktøjet bør mindst kunne rapportere:

- payload match
- byte count match
- round-trip latency
- jitter

## Bring-Up Rækkefølge

Brug denne rækkefølge i LPC54628-projektet:

1. få USB enumeration stabil
2. start stream via den eksisterende protokol
3. send én gyldig audio packet
4. verificer én korrekt loopback-packet retur
5. send en kort packet-burst
6. verificer sekvenskontrol og nul fejl-counters i healthy case
7. stop stream rent
8. kør negative tests med dårlig header, forkert længde og sequence gap

## Rolle I Daglig Verifikation

Den digitale loopback bør beholdes permanent i LPC54628-systemet som reference for daglig verifikation af den digitale vej.

Den er velegnet til at kontrollere:

- USB session health
- stream start og stop health
- packet round-trip integrity
- latency og jitter inden for forventet område
- fravær af discontinuities og buffer faults

Hvis systemet senere også skal verificere mikrofon eller codec-input, skal det ske som separate verifikationspaths oven på samme testarkitektur.

## Capture-buffer Perspektiv

Denne kodebase har allerede en playback buffer.

I LPC54628-projektet vil der sandsynligvis også blive brug for en capture buffer, hvis systemet senere skal håndtere:

- mikrofon test
- codec input test
- analog input verification

Årsagen er, at capture produceres kontinuerligt af hardware, mens USB TX consumerer packetvist og med sin egen timing.

Men capture-bufferen bør være fase 2 eller senere.
Den digitale loopback-reference bør komme først.

## Konklusion

Den rigtige måde at løfte dette over i LPC54628-kodebasen er:

- genbrug den digitale kontrakt og streamlogikken
- genbrug buffer- og metric-idéerne
- behold den eksisterende kommando/event-protokol
- skriv nye LPC54628 platform-adaptere
- brug digital loopback som permanent reference for USB-performance og daglig verifikation

Det giver maksimal læringsoverførsel fra MCXN947-projektet uden at trække MCX-specifik platformkode med over.