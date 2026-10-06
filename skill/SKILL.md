
---
name: cpm-datalake
description:
Skill per l'analisi dei dati presenti nel datalake di CPM — Usa questa skill ogni volta che l'utente fa domande su costi, ricavi, avanzamento, EVM (Earned Value Management),
budget, consuntivo, ordinato, richieste d'acquisto, margine, WBS, SAL, SIL, CPI, SPI, EAC, VAC o qualsiasi concetto legato al controllo di gestione progettuale. 
Trigger anche per frasi italiane come "quanto abbiamo speso", "confronta previsto e consuntivo", "stato del progetto", "avanzamento lavori", "margine di contribuzione", "valore guadagnato".
---
 
# CPM Datalake — Skill di Analisi del datalake di CPM
 
## Panoramica del datalake

Il datalake contiene dati relativi alle commesse gestite in CPM. Sono presenti dati di previsione (BuildingRequirements), ordinato (Orders), consuntivo (EarnCostAccountings) più altre informazioni a supporto.
il datalake è organizzato per cartelle, dentro alle cartelle ci sono file (csv, parquet, txt, md, xml) e dentro ai file si trovano i dati.

Le cartelle sono le seguenti: Projects, Products, WorkBreakdownElements, BuildingRequirements, Orders, EarnCostAccountings, PurchaseRequests, PaymentCertificates
In ogni cartella ci sono dei file nominati con GUID del progetto a cui si riferiscono (es. 71202acf-c53a-4df9-97f7-a99000a1e6cb.csv)

**IMPORTANTE** Quando l'utente ti passa il codice di un progetto, devi andare nella cartella Projects estrarre dai csv i dati di tutti i progetti e recuperare il guid identificato dal codice. Poi il guid del progetto lo userai per estrarre tutte le info da tutte le altre cartelle
 
---
 
## Architettura: cartelle, file e campi
 
### cartella Projects
un file per ciascun progetto
campi del file: ProjectId;CompanyMnemonicId;CompanyDescription;MnemoncId;Description;Date;ClosedDate;CustomerMnemoncId;CustomerDescription;ProjectManagerMnemoncId;ProjectManagerDescription;ElectricalProjectManagerMnemoncId;ElectricalProjectManagerDescription;MechanicalProjectManagerMnemoncId;MechanicalProjectManagerDescription;ConstructionProjectManagerMnemoncId;ConstructionProjectManagerDescription;ProjectCPMnemonicId1;ProjectCPDescription1;ProjectCPMnemonicId2;ProjectCPDescription2;ProjectCPMnemonicId3;ProjectCPDescription3;ProjectCPMnemonicId4;ProjectCPDescription4;ProjectCPMnemonicId5;ProjectCPDescription5;ProjectCPMnemonicId6;ProjectCPDescription6;ProjectCPMnemonicId7;ProjectCPDescription7;ProjectCPMnemonicId8;ProjectCPDescription8;ProjectCPMnemonicId9;ProjectCPDescription9;ProjectCPMnemonicId10;ProjectCPDescription10;Address;Location;PostalCode;ProvinceCode;CountryCode;ProjectManagerMail;DesignerMnemonicId;DesignerDescription;CutoffDate;GeoPosition
 
### cartella Products
un file per ciascun progetto
campi del file: ProductId;MnemonicId;Description;UnitMnemonicId;UnitDescription;ClassificationDescription;LabourPerc;EquipmentPerc;MaterialPerc;SafetyBurdenPerc;PlannedUnitCost;ProductCPMnemonicId1;ProductCPDescription1;ProductCPMnemonicId2;ProductCPDescription2;ProductCPMnemonicId3;ProductCPDescription3;ProductCPMnemonicId4;ProductCPDescription4;ProductCPMnemonicId5;ProductCPDescription5;ProductCPMnemonicId6;ProductCPDescription6;ProductCPMnemonicId7;ProductCPDescription7;ProductCPMnemonicId8;ProductCPDescription8;ProductCPMnemonicId9;ProductCPDescription9;ProductCPMnemonicId10;ProductCPDescription10;CompanyMnemonicId;ProjectMnemonicId;ProductTypeId;ProductTypeDescription;IsSafetyBurden;DiscountPerc

### cartella WorkBreakdownElements
un file per ciascun progetto
campi del file: WorkBreakdownElementId;MnemonicId;Description;WorkBreakdownElementCPMnemonicId1;WorkBreakdownElementCPDescription1;WorkBreakdownElementCPMnemonicId2;WorkBreakdownElementCPDescription2;WorkBreakdownElementCPMnemonicId3;WorkBreakdownElementCPDescription3;WorkBreakdownElementCPMnemonicId4;WorkBreakdownElementCPDescription4;WorkBreakdownElementCPMnemonicId5;WorkBreakdownElementCPDescription5;WorkBreakdownElementCPMnemonicId6;WorkBreakdownElementCPDescription6;WorkBreakdownElementCPMnemonicId7;WorkBreakdownElementCPDescription7;WorkBreakdownElementCPMnemonicId8;WorkBreakdownElementCPDescription8;WorkBreakdownElementCPMnemonicId9;WorkBreakdownElementCPDescription9;WorkBreakdownElementCPMnemonicId10;WorkBreakdownElementCPDescription10;CompanyMnemonicId;ProjectMnemonicId

### cartella BuildingRequirements
un file per ciascun progetto
campi del file: ProjectId;ProductId;WorkBreakdownElementId;MnemonicId;Description;NominativeMnemonicId;NominativeDescription;CategoryMnemonicId;CategoryDescription;Date;Month;Year;AccountingType;EarnAmount;EarnQuantity;CostAmount;CostQuantity;BuildingRequirementCPMnemonicId1;BuildingRequirementCPDescription1;BuildingRequirementCPMnemonicId2;BuildingRequirementCPDescription2;BuildingRequirementCPMnemonicId3;BuildingRequirementCPDescription3;BuildingRequirementCPMnemonicId4;BuildingRequirementCPDescription4;BuildingRequirementCPMnemonicId5;BuildingRequirementCPDescription5;BuildingRequirementCPMnemonicId6;BuildingRequirementCPDescription6;BuildingRequirementCPMnemonicId7;BuildingRequirementCPDescription7;BuildingRequirementCPMnemonicId8;BuildingRequirementCPDescription8;BuildingRequirementCPMnemonicId9;BuildingRequirementCPDescription9;BuildingRequirementCPMnemonicId10;BuildingRequirementCPDescription10;SOAMnemonicId;SOADescription;StartDate;EndDate

### cartella Orders
un file per ciascun progetto
campi del file: ProjectId;ProductId;WorkBreakdownElementId;MnemonicId;Description;NominativeMnemonicId;NominativeDescription;CategoryMnemonicId;CategoryDescription;Date;Month;Year;AccountingType;EarnAmount;EarnQuantity;CostAmount;CostQuantity;OrderCPMnemonicId1;OrderCPDescription1;OrderCPMnemonicId2;OrderCPDescription2;OrderCPMnemonicId3;OrderCPDescription3;OrderCPMnemonicId4;OrderCPDescription4;OrderCPMnemonicId5;OrderCPDescription5;OrderCPMnemonicId6;OrderCPDescription6;OrderCPMnemonicId7;OrderCPDescription7;OrderCPMnemonicId8;OrderCPDescription8;OrderCPMnemonicId9;OrderCPDescription9;OrderCPMnemonicId10;OrderCPDescription10;SOAMnemonicId;SOADescription;ToDeliverCostQuantity;ToDeliverCostAmount;ContractStartDate;ContractEndDate;ContractNewEndDate;ContractNo;CompanyMnemonicId

### cartella EarnCostAccountings
un file per ciascun progetto
campi del file: ProjectId;ProductId;WorkBreakdownElementId;MnemonicId;Description;NominativeMnemonicId;NominativeDescription;CategoryMnemonicId;CategoryDescription;ResourceMnemonicId;ResourceDescription;Date;Month;Year;WBSMnemonicId;WBSDescription;AccountingType;JPSNumber;JPSAmount;JPSQuantity;JISNumber;JISAmount;JISQuantity;CostAmount;CostQuantity;AccountingCPMnemonicId1;AccountingCPDescription1;AccountingCPMnemonicId2;AccountingCPDescription2;AccountingCPMnemonicId3;AccountingCPDescription3;AccountingCPMnemonicId4;AccountingCPDescription4;AccountingCPMnemonicId5;AccountingCPDescription5;AccountingCPMnemonicId6;AccountingCPDescription6;AccountingCPMnemonicId7;AccountingCPDescription7;AccountingCPMnemonicId8;AccountingCPDescription8;AccountingCPMnemonicId9;AccountingCPDescription9;AccountingCPMnemonicId10;AccountingCPDescription10;JPSIsCurrent;JISIsCurrent;SOAMnemonicId;SOADescription;ERPDocumentId;ERPDocumentMeasurementId;ResourceType;ContractStartDate;ContractEndDate;ContractNewEndDate;ContractNo;OrderRef;OrderMeasurementRef;AccountingTemporaryAssociationType;RevisionDate;ERPAccountingId;RowId;SubDescription;CompanyMnemonicId

### cartella PurchaseRequests
un file per ciascun progetto
campi del file: ProjectId;ProductId;WorkBreakdownElementId;MnemonicId;Description;NominativeMnemonicId;NominativeDescription;ApplicantMnemonicId;ApplicantDescription;CategoryMnemonicId;CategoryDescription;CreationDate;CreationMonth;CreationYear;DeliveryDate;DeliveryMonth;DeliveryYear;AccountingType;CostAmount;CostQuantity;PurchaseRequestCPMnemonicId1;PurchaseRequestCPDescription1;PurchaseRequestCPMnemonicId2;PurchaseRequestCPDescription2;PurchaseRequestCPMnemonicId3;PurchaseRequestCPDescription3;PurchaseRequestCPMnemonicId4;PurchaseRequestCPDescription4;PurchaseRequestCPMnemonicId5;PurchaseRequestCPDescription5;PurchaseRequestCPMnemonicId6;PurchaseRequestCPDescription6;PurchaseRequestCPMnemonicId7;PurchaseRequestCPDescription7;PurchaseRequestCPMnemonicId8;PurchaseRequestCPDescription8;PurchaseRequestCPMnemonicId9;PurchaseRequestCPDescription9;PurchaseRequestCPMnemonicId10;PurchaseRequestCPDescription10;CompanyMnemonicId

### cartella PaymentCertificates
un file per ciascun progetto
campi del file: ProjectId;AccountingMnemonicId;AccountingDescription;MnemonicId;Description;CertificateType;CertificateDate;InstalmentNo;InstalmentDate;AccountingType;NominativeMnemonicId;NominativeDescription;JPSNumber;JPSDate;NetAmount;RoundedAmount;RoundedWithVatAmount;GrossAmount;AnticipationRecoveryAmount;WorkersInsuranceAmount;WithholdingdGuaranteeAmount;AccountingRevisionAmount;DefinitiveAccountingRevisionAmount;Open1Amount;Open2Amount;Open3Amount;CompanyMnemonicId;ERPDocumentId;ERPTransferDate
 
---
 
## Terminologia Italiana ↔ Campi/Calcoli
 
| Termine italiano | Campo/Calcolo |
|-----------------|-------------|
| costo consuntivo / ammontare costo consuntivo | `EarnCostAccountings[CostAmount]`|
| data consuntivo | `EarnCostAccountings[Date]`|
| costo previsto / budget | `BuildingRequirements[CostAmount]`|
| data previsione | `BuildingRequirements[Date]`|
| costo ordinato | `Orders[CostAmount]`|
| data ordinato | `Orders[Date]`|
| costo richiesto | `PurchaseRequests[CostAmount]` |
| data richiesta | `PurchaseRequests[CreationDate]` |
| ricavo consuntivo (SAL) | `EarnCostAccountings[JPSAmount]`|
| ricavo interno (SIL) | `EarnCostAccountings[JISAmount]`|
| ricavo previsto | `BuildingRequirements[EarnAmount]`|
| quantità SAL | `EarnCostAccountings[JPSQuantity]`|
| quantità SIL | `EarnCostAccountings[JISQuantity]`|
| Earned Value / valore guadagnato | `EarnCostAccountings[EVAmount]`|
| margine di contribuzione (MDC) | calcolo: ricavo consuntivo (SAL) - costo consuntivo|
| % avanzamento costo | calcolo: costo consuntivo / costo previsto * 100 |
| data di riferimento EVM | `Projects[CutoffDate]` |
 
---
 
## Calcoli Chiave per l'analisi dati
 
### Costi
- costo consuntivo totale = somma di `EarnCostAccountings[CostAmount]`
- costo previsto/budget = somma di `BuildingRequirements[CostAmount]`
- % speso vs budget (somma di `BuildingRequirements[CostAmount]` / somma di `BuildingRequirements[CostAmount]`)
### Ricavi e Margine
- ricavo consuntivo JPS (esterno/ufficiale) = somma di `EarnCostAccountings[JPSAmount]`
- ricavo consuntivo JIS (interno) = somma di `EarnCostAccountings[JISAmount]`
- ricavo previsto = somma di `BuildingRequirements[EarnAmount]`
- margine contribuzione consuntivo = somma di `EarnCostAccountings[JPSAmount]` - somma di `EarnCostAccountings[CostAmount]`
- margine contribuzione previsto = somma di `BuildingRequirements[EarnAmount]` - somma di `BuildingRequirements[CostAmount]`
### EVM (Earned Value Management)
- `BAC` — Budget at Completion (budget totale)
- `PV` — Planned Value (valore pianificato a data cutoff)
- `EV` — Earned Value (valore guadagnato a data cutoff)
- `AC` — Actual Cost (costo effettivo a data cutoff)
- `CPI` — Cost Performance Index (`EV/AC`): >1 sotto budget, <1 sopra budget
- `SPI` — Schedule Performance Index (`EV/PV`): >1 in anticipo, <1 in ritardo
- `EAC1` — Estimate At Completion (`AC + (BAC - EV)`)
- `VAC1` — Variance At Completion (`EAC1 - BAC`)

---
 
## Logica EVM — Spiegazione
 
Il modello implementa EVM con CutoffDate come data di riferimento:
 
```
PV = Σ BuildingRequirements[CostAmount] dove BuildingRequirements[Date] ≤ Projects[CutoffDate]
AC = Σ EarnCostAccountings[CostAmount]  dove EarnCostAccountings[Date]  ≤ Projects[CutoffDate]
EV = Σ EarnCostAccountings[EVAmount]    dove EarnCostAccountings[Date]  ≤ Projects[CutoffDate]
CPI = EV / AC  (>1 = efficiente, <1 = inefficiente)
SPI = EV / PV  (>1 = in anticipo, <1 = in ritardo)
EAC = AC + (BAC - EV)      [metodo 1: ottimistico]
VAC = EAC - BAC             (<0 = sforamento previsto)
```
**Importante:** Per l'EVM deve essere valorizzato `Projects[CutoffDate]`.
 
---
 
## Relazioni tra Tabelle
 
**BuildingRequirements:** riferisce Project (ProjectId), riferisce Products (ProductId), riferisce WorkBreakdownElements (WorkBreakdownElementId)
**Orders:** riferisce Project (ProjectId), riferisce Products (ProductId), riferisce WorkBreakdownElements (WorkBreakdownElementId)
**PurchaseRequests:** riferisce Project (ProjectId), riferisce Products (ProductId), riferisce WorkBreakdownElements (WorkBreakdownElementId)
**EarnCostAccountings:** riferisce Project (ProjectId), riferisce Products (ProductId), riferisce WorkBreakdownElements (WorkBreakdownElementId)
**PaymentCertificates:** riferisce Project (ProjectId)
 
---

## Lettura dei file e paginazione

I tool `read_csv`, `read_parquet`, `read_text`, `read_markdown` e `read_xml` non restituiscono mai
un file intero in un'unica chiamata: si fermano a `maxRows`/`maxLines` righe (default 200) per non
superare il limite di dimensione delle risposte. Quando ci sono altre righe da leggere, il
risultato riporta `truncated: true` e un `nextOffset`.

`read_text` e `read_markdown` sono utili per leggere eventuali file di documentazione o note
(.txt/.md) presenti nel datalake accanto ai dati csv/parquet. `read_xml`/`get_xml_schema` leggono
file XML tabellari (una radice con elementi record ripetuti); se il nome dell'elemento record non
viene individuato correttamente in automatico, specificarlo esplicitamente col parametro
`recordElement`.

`read_csv`, `read_parquet` e `read_xml` accettano il parametro opzionale `filter`, un elenco di
condizioni `{column, operator, value}` in AND (operatori: `eq`, `ne`, `gt`, `gte`, `lt`, `lte`,
`contains`, `startswith`, `endswith`, `isnull`, `isnotnull`), es.
`[{"column":"ProjectId","operator":"eq","value":"123"}]`. Il filtro è applicato prima di
`offset`/`maxRows`, quindi la paginazione conta solo le righe filtrate. Usalo per leggere solo le
righe di un progetto invece di scorrere tutto il file. `read_text`/`read_markdown` accettano
`contains` per selezionare le righe che contengono un testo.

**IMPORTANTE — per qualsiasi calcolo aggregato (somme, conteggi, medie su `CostAmount`,
`EarnAmount`, `JPSAmount`, `JISAmount`, ecc.) è obbligatorio leggere **tutte** le righe del file**,
non solo la prima pagina, altrimenti i totali (costo consuntivo, budget, EVM, margine...) risultano
parziali e sbagliati. Procedura:

1. Chiama `read_csv`/`read_parquet`/`read_xml` con `offset: 0` (ed eventualmente un `maxRows` alto,
   fino al massimo consentito, per ridurre il numero di round-trip).
2. Se il risultato ha `truncated: true`, richiama nuovamente il tool sullo stesso file passando
   `offset: nextOffset`, e ripeti finché `truncated` non è `false`.
3. Solo dopo aver raccolto tutte le pagine esegui le somme/aggregazioni richieste.

Questo vale soprattutto per `BuildingRequirements`, `Orders`, `EarnCostAccountings` e
`PurchaseRequests`, che possono contenere molte righe per progetto.

---

## Salvataggio di file e ricerca nell'indice

Per conservare risultati di analisi nel datalake usa i tool `save_text` (.txt), `save_markdown` (.md),
`save_csv` (.csv, contenuto CSV completo con intestazione), `save_parquet` (.parquet, array di righe
oggetto colonna→valore) e `save_pdf` (.pdf, contenuto base64). Parametri comuni: `path` (comprese
cartelle/sottocartelle, create se mancanti; l'estensione deve corrispondere al formato), `description`
opzionale e `overwrite` (default false: un file esistente non viene sostituito). **Non salvare mai
dentro le cartelle dei dati di progetto** (Projects, Orders, ecc.): usa una cartella dedicata, es.
`Analisi/<progetto>/`.

Ogni salvataggio viene registrato nel file di indice `saved-files-index.csv` (radice del filesystem;
colonne `path,name,format,sizeBytes,savedAt,description`). Per ritrovare un file salvato usa
`search_file` con `nomeFile` (il nome *contiene* il testo, case-insensitive) e/o `tipoFile` (csv,
parquet, pdf, md, txt). Se viene trovato **un solo file** il tool ne restituisce anche il contenuto
(primi `maxRows` righe; se `truncated` è true prosegui con il tool `read_*` e `offset = nextOffset`;
per i pdf solo i metadati); con più file restituisce l'elenco e devi scegliere il `path` da leggere.
Per i CSV con separatore diverso da `,` passa `delimiter`. `search_file` cerca solo i file salvati con
i tool `save_*`, non quelli già presenti nel datalake.

---
 
## Best Practice per le Analisi
 
1. **Filtra sempre per progetto** — le analisi vanno limitate a progetti specifici o gruppi
2. **Verifica CutoffDate** — per EVM deve essere impostata su `Projects`
3. **Confronta Planned (BuildingRequirements) vs Actual (EarnCostAccountings)** — le misure di varianza identificano i problemi
4. **Confronta Planned (BuildingRequirements) vs Committed (Orders)** — le misure di varianza identificano i problemi
5. **Confronta Committed (Orders) vs Actual (EarnCostAccountings)** — le misure di varianza identificano i problemi
6. **Monitora CPI e SPI** — valori <1.0 segnalano criticità
7. **JPS vs JIS** — JPS è il metodo ufficiale (esterno), JIS per tracking interno
8. **Pagina sempre fino in fondo** — prima di sommare/aggregare, leggi tutte le pagine di un file
   seguendo `nextOffset` finché `truncated` è `false` (vedi sezione "Lettura dei file e paginazione")

---
 
 