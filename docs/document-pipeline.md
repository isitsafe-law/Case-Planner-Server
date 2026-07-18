# Document Pipeline

Document generation now passes through `IDocumentPipeline` in the server application.

```text
API request
   -> IDocumentPipeline
      -> IDocumentRendererRegistry (document-kind validation)
      -> IDocumentCompositionService (provider-selected data/template composition)
      -> IDocumentValidator (merge and finalization checks)
      -> IGeneratedDocumentService (snapshot metadata and file storage)
```

The current composition services remain in place so SQLite development and the SQL Server pilot use the
same application boundary. This is intentional: renderer extraction can proceed one document type at a
time without changing the client contract or forcing SQL Server activation.

## Discovery endpoint

`GET /api/document-renderers` returns built-in renderer metadata, including document kind, title, and
required manual fields. Client screens should prefer this endpoint over a second hard-coded catalog.

## Validation rules

- Standard document kinds must exist in the server renderer registry.
- Custom requests must provide a template key.
- Saved documents must provide a kind and title.
- Finalized documents must contain rendered text and cannot retain `[MISSING: ...]` placeholders.

## Next extraction step

Extract the Interrogatories composition into a dedicated renderer implementing the registry contract. Keep
the existing endpoint response shapes and snapshot provenance unchanged while the renderer is moved.
