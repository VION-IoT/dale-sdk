2026-11-05 · review · mbus-service-provider · The review of ecocoach's M-Bus proposal held that option B needs a Vion.Contracts and dale release for every new unit; the operator said units carried as strings, as in Beckhoff's ST_MBus_Data, keep that wire stable, and asked for it as the fallback.

2026-11-05 · review · mbus-service-provider · The review routed the M-Bus contract to an architecture spec, citing the dale release new Vion.Contracts types need and the ST provider as a second repo; the operator said every dale-sdk change needs a dale update and the ST provider is another org's, outside the process, and asked for a dale-sdk change doc.

2026-11-05 · review · mbus-service-provider · The review read VION-437's minimal TwinCAT HAL as a possible second ST M-Bus master beside the TwinCAT spec's phase 5; the operator said minimal means hard-coded hardware for the first project and it is v1 M-Bus support.

2026-11-10 · decision · mbus-service-provider · Wire option A settled for the M-Bus contract: the ST bench read all five EM530 telegrams raw through Tc2_MBus 3.5.4's frame buffer, so no provider has to decode.

2026-11-10 · review · mbus-service-provider · The M-Bus change doc on #281 planned no commissioning tool and no documentation; the operator asked for a debug client like the Modbus TCP example's and a guide section in the documentation repo.

2026-11-10 · decision · mbus-service-provider · The M-Bus example and its debug client go to Cloud as the Modbus TCP example does; this change adds no production upload.
