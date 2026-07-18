using System.Text.Json;
using Microsoft.Data.Sqlite;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Build-plan step 3 (data model cutover): SQLite schema for the unified document platform.
// This is the local-development shape only - SQL Server is the production target (see
// server/CasePlanner.DatabaseMigrator/Sql/023_document_platform.sql and
// docs/sql-server-migration.md), authored first and kept in sync with this file by hand, the
// same way every other table in this codebase is dual-provider. Kept in its own partial file
// (CasePlannerRepository already isn't partial anywhere else useful to touch) rather than folded
// into the enormous existing SchemaSql string, so this genuinely new surface has a reviewable
// diff of its own.
public sealed partial class CasePlannerRepository
{
    private const string DocumentPlatformSchemaSql = """
        CREATE TABLE IF NOT EXISTS document_templates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            template_key TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL,
            description TEXT,
            category TEXT NOT NULL DEFAULT 'Other',
            document_type TEXT,
            is_builtin INTEGER NOT NULL DEFAULT 0,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            created_by TEXT
        );
        CREATE TABLE IF NOT EXISTS document_template_versions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            template_id INTEGER NOT NULL REFERENCES document_templates(id),
            version INTEGER NOT NULL,
            storage_path TEXT NOT NULL,
            tokens_json TEXT NOT NULL DEFAULT '[]',
            unknown_tokens_json TEXT NOT NULL DEFAULT '[]',
            is_active INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            created_by TEXT,
            UNIQUE(template_id, version)
        );
        CREATE TABLE IF NOT EXISTS document_runtime_inputs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            template_version_id INTEGER NOT NULL REFERENCES document_template_versions(id),
            field_key TEXT NOT NULL,
            label TEXT NOT NULL,
            field_type TEXT NOT NULL DEFAULT 'text',
            is_required INTEGER NOT NULL DEFAULT 1,
            sort_order INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS document_template_sections (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            template_version_id INTEGER NOT NULL REFERENCES document_template_versions(id),
            section_key TEXT NOT NULL,
            label TEXT NOT NULL,
            description TEXT,
            issue_tag_name TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0,
            UNIQUE(template_version_id, section_key)
        );
        -- section_a_id/section_b_id: the pair is unordered (A overlaps B is the same fact as B
        -- overlaps A). The CHECK forces callers to store the smaller id first, so the UNIQUE
        -- constraint actually catches a reversed duplicate insert instead of silently allowing it.
        CREATE TABLE IF NOT EXISTS document_section_overlaps (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            section_a_id INTEGER NOT NULL REFERENCES document_template_sections(id),
            section_b_id INTEGER NOT NULL REFERENCES document_template_sections(id),
            note TEXT,
            CHECK (section_a_id < section_b_id),
            UNIQUE(section_a_id, section_b_id)
        );
        CREATE TABLE IF NOT EXISTS document_generations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            template_id INTEGER NOT NULL REFERENCES document_templates(id),
            template_version_id INTEGER NOT NULL REFERENCES document_template_versions(id),
            output_path TEXT NOT NULL,
            rendered_at TEXT NOT NULL,
            generated_by TEXT,
            sections_included_json TEXT NOT NULL DEFAULT '[]',
            runtime_input_values_json TEXT NOT NULL DEFAULT '{}',
            is_draft INTEGER NOT NULL DEFAULT 1,
            is_finalized INTEGER NOT NULL DEFAULT 0,
            missing_fields_json TEXT NOT NULL DEFAULT '[]'
        );
        CREATE INDEX IF NOT EXISTS ix_document_template_versions_template ON document_template_versions(template_id);
        CREATE INDEX IF NOT EXISTS ix_document_runtime_inputs_version ON document_runtime_inputs(template_version_id);
        CREATE INDEX IF NOT EXISTS ix_document_template_sections_version ON document_template_sections(template_version_id);
        CREATE INDEX IF NOT EXISTS ix_document_generations_case ON document_generations(case_id);
        """;

    // The tag vocabulary was fixed and code-seeded before this cutover (Phase 1 audit: "no
    // create-tag endpoint exists"). This is that endpoint's SQLite backing - an attorney adding
    // a new issue tag no longer requires a developer and a redeploy.
    public async Task<IssueTagRecord> CreateIssueTagAsync(string name, string? description, string? category)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A tag name is required.", nameof(name));
        }

        return await WithWriteAsync(async (connection, tx) =>
        {
            var check = connection.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM issue_tags WHERE name = @name COLLATE NOCASE AND is_deleted = 0";
            check.Parameters.AddWithValue("@name", name);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            {
                throw new DuplicateIssueTagException($"An issue tag named '{name}' already exists.");
            }

            var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO issue_tags (name, description, category) VALUES (@name, @description, @category);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            insert.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync());

            return new IssueTagRecord { Id = id, Name = name, Description = description, Category = category };
        });
    }

    // ---- Build-plan step 5 (unified Settings UI): Issue Tags admin ----

    public async Task<IssueTagRecord> RenameIssueTagAsync(long id, string name, string? description, string? category)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A tag name is required.", nameof(name));
        }

        return await WithWriteAsync(async (connection, tx) =>
        {
            var check = connection.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM issue_tags WHERE name = @name COLLATE NOCASE AND is_deleted = 0 AND id <> @id";
            check.Parameters.AddWithValue("@name", name);
            check.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            {
                throw new DuplicateIssueTagException($"An issue tag named '{name}' already exists.");
            }

            var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE issue_tags SET name = @name, description = @description, category = @category WHERE id = @id AND is_deleted = 0";
            update.Parameters.AddWithValue("@name", name);
            update.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            update.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
            update.Parameters.AddWithValue("@id", id);
            if (await update.ExecuteNonQueryAsync() == 0)
            {
                throw new InvalidOperationException("Issue tag not found.");
            }

            return new IssueTagRecord { Id = id, Name = name, Description = description, Category = category };
        });
    }

    // Soft-delete only: case history (case_issue_tags) and document_template_sections may still
    // reference this tag by name/id, and neither should have a dangling or silently-wrong reference
    // after retirement. EnsureIssueTagCatalogAsync's "insert if no row exists with this name" seed
    // check already leaves a retired row alone rather than resurrecting it.
    public async Task RetireIssueTagAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE issue_tags SET is_deleted = 1 WHERE id = @id";
            update.Parameters.AddWithValue("@id", id);
            if (await update.ExecuteNonQueryAsync() == 0)
            {
                throw new InvalidOperationException("Issue tag not found.");
            }

            return 0;
        });
    }

    // "See which templates reference it" (build-plan step 5) - document_template_sections.issue_tag_name
    // is matched by string equality against issue_tags.name, same convention as the pre-existing
    // checklist-template trigger matching, so this is a plain-text join, not a real FK join.
    public async Task<List<IssueTagUsage>> GetIssueTagUsageAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT dts.issue_tag_name, dt.title
            FROM document_template_sections dts
            JOIN document_template_versions dtv ON dtv.id = dts.template_version_id
            JOIN document_templates dt ON dt.id = dtv.template_id
            WHERE dts.issue_tag_name IS NOT NULL AND dt.is_deleted = 0
            """;
        var byTag = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var tag = reader.GetString(0);
                var title = reader.GetString(1);
                if (!byTag.TryGetValue(tag, out var list)) byTag[tag] = list = [];
                if (!list.Contains(title)) list.Add(title);
            }
        }

        return byTag.Select(kv => new IssueTagUsage { TagName = kv.Key, TemplateTitles = kv.Value }).ToList();
    }

    // ---- Build-plan step 4 (unified case UI): the generation pipeline ----

    private sealed record ActiveTemplateVersion(DocumentTemplateRecord Template, DocumentTemplateVersionRecord Version);

    private static async Task<ActiveTemplateVersion?> GetActiveTemplateVersionAsync(SqliteConnection connection, string templateKey)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.template_key, t.title, t.description, t.category, t.document_type, t.is_builtin, t.created_at, t.created_by,
                   v.id, v.version, v.storage_path, v.tokens_json, v.unknown_tokens_json, v.created_at, v.created_by
            FROM document_templates t
            JOIN document_template_versions v ON v.template_id = t.id AND v.is_active = 1
            WHERE t.template_key = @key AND t.is_deleted = 0
            """;
        cmd.Parameters.AddWithValue("@key", templateKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var template = new DocumentTemplateRecord
        {
            Id = reader.GetInt64(0),
            TemplateKey = reader.GetString(1),
            Title = reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            Category = reader.GetString(4),
            DocumentType = reader.IsDBNull(5) ? null : reader.GetString(5),
            IsBuiltin = reader.GetInt64(6) == 1,
            CreatedAt = reader.GetString(7),
            CreatedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
        };
        var version = new DocumentTemplateVersionRecord
        {
            Id = reader.GetInt64(9),
            TemplateId = template.Id,
            Version = reader.GetInt32(10),
            StoragePath = reader.GetString(11),
            Tokens = JsonSerializer.Deserialize<List<string>>(reader.GetString(12)) ?? [],
            UnknownTokens = JsonSerializer.Deserialize<List<string>>(reader.GetString(13)) ?? [],
            IsActive = true,
            CreatedAt = reader.GetString(14),
            CreatedBy = reader.IsDBNull(15) ? null : reader.GetString(15),
        };
        return new ActiveTemplateVersion(template, version);
    }

    private static async Task<List<DocumentTemplateSectionRecord>> GetTemplateSectionsAsync(SqliteConnection connection, long templateVersionId)
    {
        var list = new List<DocumentTemplateSectionRecord>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, template_version_id, section_key, label, description, issue_tag_name, sort_order
            FROM document_template_sections WHERE template_version_id = @v ORDER BY sort_order, label
            """;
        cmd.Parameters.AddWithValue("@v", templateVersionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DocumentTemplateSectionRecord
            {
                Id = reader.GetInt64(0),
                TemplateVersionId = reader.GetInt64(1),
                SectionKey = reader.GetString(2),
                Label = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                IssueTagName = reader.IsDBNull(5) ? null : reader.GetString(5),
                SortOrder = reader.GetInt32(6),
            });
        }

        return list;
    }

    private static async Task<List<DocumentRuntimeInputRecord>> GetRuntimeInputsAsync(SqliteConnection connection, long templateVersionId)
    {
        var list = new List<DocumentRuntimeInputRecord>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, template_version_id, field_key, label, field_type, is_required, sort_order
            FROM document_runtime_inputs WHERE template_version_id = @v ORDER BY sort_order, label
            """;
        cmd.Parameters.AddWithValue("@v", templateVersionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DocumentRuntimeInputRecord
            {
                Id = reader.GetInt64(0),
                TemplateVersionId = reader.GetInt64(1),
                FieldKey = reader.GetString(2),
                Label = reader.GetString(3),
                FieldType = reader.GetString(4),
                IsRequired = reader.GetInt64(5) == 1,
                SortOrder = reader.GetInt32(6),
            });
        }

        return list;
    }

    // Maps each section to the human-readable overlap warnings the generation checklist shows -
    // "may overlap with: Access" - rather than the raw id pairing document_section_overlaps
    // actually stores. Declarative, authored once by whoever writes the two sections; not
    // automatic text-similarity detection (see Architecture: "a declarative overlap flag").
    private static async Task<Dictionary<string, List<string>>> GetOverlapWarningsAsync(SqliteConnection connection, List<DocumentTemplateSectionRecord> sections)
    {
        var warnings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (sections.Count == 0) return warnings;

        var idToKey = sections.ToDictionary(s => s.Id, s => s.SectionKey);
        var idToLabel = sections.ToDictionary(s => s.Id, s => s.Label);
        var ids = sections.Select(s => s.Id).ToList();
        var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));

        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT section_a_id, section_b_id, note FROM document_section_overlaps WHERE section_a_id IN ({placeholders}) OR section_b_id IN ({placeholders})";
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@id{i}", ids[i]);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var a = reader.GetInt64(0);
            var b = reader.GetInt64(1);
            if (!idToKey.TryGetValue(a, out var keyA) || !idToKey.TryGetValue(b, out var keyB)) continue;
            var note = reader.IsDBNull(2) ? null : reader.GetString(2);
            var suffix = string.IsNullOrWhiteSpace(note) ? "" : $" ({note})";
            Add(warnings, keyA, $"May overlap with '{idToLabel[b]}'{suffix}.");
            Add(warnings, keyB, $"May overlap with '{idToLabel[a]}'{suffix}.");
        }

        return warnings;

        static void Add(Dictionary<string, List<string>> dict, string key, string value)
        {
            if (!dict.TryGetValue(key, out var list)) dict[key] = list = [];
            list.Add(value);
        }
    }

    public async Task<DocumentGenerationChecklist?> GetDocumentGenerationChecklistAsync(long caseId, string templateKey)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var active = await GetActiveTemplateVersionAsync(connection, templateKey);
        if (active is null) return null;

        var sections = await GetTemplateSectionsAsync(connection, active.Version.Id);
        var runtimeInputs = await GetRuntimeInputsAsync(connection, active.Version.Id);
        var overlaps = await GetOverlapWarningsAsync(connection, sections);
        var caseTags = (await GetCaseIssueTagsAsync(caseId)).Select(t => t.TagName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DocumentGenerationChecklist
        {
            TemplateKey = active.Template.TemplateKey,
            Title = active.Template.Title,
            TemplateVersion = active.Version.Version,
            RuntimeInputs = runtimeInputs,
            Sections = sections.Select(s => new DocumentGenerationChecklistItem
            {
                SectionKey = s.SectionKey,
                Label = s.Label,
                Description = s.Description,
                IssueTagName = s.IssueTagName,
                IsDefaultChecked = s.IssueTagName is not null && caseTags.Contains(s.IssueTagName),
                OverlapWarnings = overlaps.TryGetValue(s.SectionKey, out var w) ? w : [],
            }).ToList(),
        };
    }

    public async Task<DocumentGenerationResult> GenerateDocumentPlatformDocumentAsync(
        long caseId, string templateKey, List<string> selectedSectionKeys, Dictionary<string, string> runtimeInputValues, string? outputFileName)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var org = await GetOrgDefaultsAsync();

        return await WithWriteAsync(async (connection, tx) =>
        {
            var active = await GetActiveTemplateVersionAsync(connection, templateKey)
                ?? throw new InvalidOperationException($"No active template found for key '{templateKey}'.");
            var runtimeInputDefs = await GetRuntimeInputsAsync(connection, active.Version.Id);

            foreach (var required in runtimeInputDefs.Where(r => r.IsRequired))
            {
                if (!runtimeInputValues.TryGetValue(required.FieldKey, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"'{required.Label}' is required to generate this document.");
                }
            }

            var manualFieldDefs = runtimeInputDefs.Select(r => new DocumentTemplateField { Key = r.FieldKey, Label = r.Label, Type = r.FieldType }).ToList();
            var tokens = DocumentGenerationEngine.BuildTokens(workspace.Case, org, runtimeInputValues, manualFieldDefs);
            var context = MergeContextBuilder.Build(tokens, selectedSectionKeys);

            var templateBytes = await File.ReadAllBytesAsync(active.Version.StoragePath);
            var merged = DocxSectionMerger.Render(templateBytes, context, out var missing);

            // Millisecond timestamp alone isn't enough - two generations of the same case/template
            // in quick succession (a user double-clicking Generate) can land in the same tick, so
            // a short random suffix guarantees distinct file names even then.
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var unique = Guid.NewGuid().ToString("N")[..6];
            var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(outputFileName) ? active.Template.Title : outputFileName!);
            var fileName = $"{baseName}_{stamp}_{unique}.docx";
            var outputPath = _documents.CreatePath(caseId, fileName);
            await _documents.WriteBytesAsync(outputPath, merged);

            try
            {
                var now = DateTime.UtcNow.ToString("O");
                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO document_generations
                        (case_id, template_id, template_version_id, output_path, rendered_at, generated_by,
                         sections_included_json, runtime_input_values_json, is_draft, is_finalized, missing_fields_json)
                    VALUES (@caseId, @templateId, @versionId, @path, @now, @by, @sections, @inputs, 1, 0, @missing);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("@caseId", caseId);
                insert.Parameters.AddWithValue("@templateId", active.Template.Id);
                insert.Parameters.AddWithValue("@versionId", active.Version.Id);
                insert.Parameters.AddWithValue("@path", outputPath);
                insert.Parameters.AddWithValue("@now", now);
                insert.Parameters.AddWithValue("@by", _actor.AuditLabel);
                insert.Parameters.AddWithValue("@sections", JsonSerializer.Serialize(selectedSectionKeys));
                insert.Parameters.AddWithValue("@inputs", JsonSerializer.Serialize(runtimeInputValues));
                insert.Parameters.AddWithValue("@missing", JsonSerializer.Serialize(missing));
                var id = Convert.ToInt64(await insert.ExecuteScalarAsync());

                return new DocumentGenerationResult
                {
                    GenerationId = id,
                    OutputPath = outputPath,
                    SectionsIncluded = selectedSectionKeys,
                    MissingFields = missing,
                };
            }
            catch
            {
                // Matches the established custom-docx-generation pattern: a file with no
                // corresponding history row is worse than no file at all.
                await _documents.DeleteIfExistsAsync(outputPath);
                throw;
            }
        });
    }

    public async Task<DocumentGenerationRecord?> GetDocumentGenerationByIdAsync(long id)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, template_id, template_version_id, output_path, rendered_at, generated_by,
                   sections_included_json, runtime_input_values_json, is_draft, is_finalized, missing_fields_json
            FROM document_generations WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new DocumentGenerationRecord
        {
            Id = reader.GetInt64(0),
            CaseId = reader.GetInt64(1),
            TemplateId = reader.GetInt64(2),
            TemplateVersionId = reader.GetInt64(3),
            OutputPath = reader.GetString(4),
            RenderedAt = reader.GetString(5),
            GeneratedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
            SectionsIncluded = JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
            RuntimeInputValuesJson = reader.GetString(8),
            IsDraft = reader.GetInt64(9) == 1,
            IsFinalized = reader.GetInt64(10) == 1,
            MissingFields = JsonSerializer.Deserialize<List<string>>(reader.GetString(11)) ?? [],
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Document" : cleaned;
    }

    // ---- Seed template (proves the pipeline end-to-end; re-authoring the real built-ins is
    // build-plan step 6, not this) ----

    // "Drainage" is one of the 22 fixed issue tags already in the catalog (EnsureIssueTagCatalogAsync).
    // Tying the seed section to it means a case tagged Drainage lights this section up in the
    // checklist automatically, with zero additional wiring - exactly the mechanism this whole
    // rebuild exists to prove.
    private async Task EnsureDocumentPlatformSeedAsync(SqliteConnection connection)
    {
        var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM document_templates WHERE template_key = 'interrogatories_platform'";
        if (Convert.ToInt32(await countCmd.ExecuteScalarAsync()) > 0) return;

        var folder = Path.Combine(_paths.Config.DocumentTemplatesFolder, "platform");
        Directory.CreateDirectory(folder);
        var storagePath = Path.Combine(folder, "interrogatories_platform_v1.docx");
        await File.WriteAllBytesAsync(storagePath, BuildSeedInterrogatoriesDocx());

        var now = DateTime.UtcNow.ToString("O");
        var templateCmd = connection.CreateCommand();
        templateCmd.CommandText = """
            INSERT INTO document_templates (template_key, title, description, category, document_type, is_builtin, created_at)
            VALUES ('interrogatories_platform', 'Interrogatories & Requests for Production (Unified Platform)',
                    'Seed template proving the unified document platform end to end - see build-plan step 4.',
                    'Discovery', 'interrogatories', 1, @now);
            SELECT last_insert_rowid();
            """;
        templateCmd.Parameters.AddWithValue("@now", now);
        var templateId = Convert.ToInt64(await templateCmd.ExecuteScalarAsync());

        var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = """
            INSERT INTO document_template_versions (template_id, version, storage_path, tokens_json, is_active, created_at)
            VALUES (@templateId, 1, @path, @tokens, 1, @now);
            SELECT last_insert_rowid();
            """;
        versionCmd.Parameters.AddWithValue("@templateId", templateId);
        versionCmd.Parameters.AddWithValue("@path", storagePath);
        versionCmd.Parameters.AddWithValue("@tokens", JsonSerializer.Serialize(new[] { "County", "CaseNumber", "DefendantNames", "AttorneyName" }));
        versionCmd.Parameters.AddWithValue("@now", now);
        var versionId = Convert.ToInt64(await versionCmd.ExecuteScalarAsync());

        var sectionCmd = connection.CreateCommand();
        sectionCmd.CommandText = """
            INSERT INTO document_template_sections (template_version_id, section_key, label, description, issue_tag_name, sort_order)
            VALUES (@versionId, 'Drainage', 'Drainage', 'Additional interrogatory about surface-water drainage changes caused by the taking or construction.', 'Drainage', 1)
            """;
        sectionCmd.Parameters.AddWithValue("@versionId", versionId);
        await sectionCmd.ExecuteNonQueryAsync();
    }

    // Content for the always-present questions and the Drainage section reuses the exact wording
    // already drafted (and attorney-review-flagged) in IssueTagDiscoveryContent.cs, rather than
    // inventing new legal language for this seed.
    private static byte[] BuildSeedInterrogatoriesDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    new Run(new RunProperties(new Bold()), new Text("IN THE CIRCUIT COURT OF {{COUNTY}} COUNTY, ARKANSAS"))),
                new Paragraph(new Run(new RunProperties(new Bold()), new Text("CASE NO. {{CaseNumber}}"))),
                new Paragraph(new Run(new RunProperties(new Bold()), new Text("{{DefendantNames}}"))),
                new Paragraph(),
                new Paragraph(new Run(new RunProperties(new Bold(), new Underline { Val = UnderlineValues.Single }),
                    new Text("INTERROGATORIES AND REQUESTS FOR PRODUCTION"))),
                new Paragraph(),
                SeqInterrogatory("Please state the name, address, and telephone number of the person(s) answering these interrogatories."),
                SeqInterrogatory("Please state what the highest and best use of the property acquired by plaintiff was immediately prior to the acquisition."),
                new Paragraph(new Run(new Text("{{#Drainage}}"))),
                SeqInterrogatory("Describe any change in surface water drainage across the property that you contend resulted from the taking or construction, and any resulting damage."),
                new Paragraph(new Run(new Text("{{/Drainage}}"))),
                SeqInterrogatory("Please list each and every appraisal you have obtained for the property, including the name, address, and telephone number of the appraiser and the date of the appraisal."),
                new Paragraph(),
                new Paragraph(new Run(new Text("Respectfully submitted,"))),
                new Paragraph(new Run(new Text("{{AttorneyName}}"))));
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph SeqInterrogatory(string questionText) => new(
        new Run(new Text("INTERROGATORY NO. ")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
        new Run(new FieldCode(" SEQ Interrogatory ") { Space = SpaceProcessingModeValues.Preserve }),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
        new Run(new Text("1")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
        new Run(new Text($": {questionText}")));

    private static Paragraph SeqRequestForProduction(string requestText) => new(
        new Run(new Text("REQUEST FOR PRODUCTION NO. ")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
        new Run(new FieldCode(" SEQ RequestForProduction ") { Space = SpaceProcessingModeValues.Preserve }),
        new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
        new Run(new Text("1")),
        new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
        new Run(new Text($": {requestText}")));

    // ---- Build-plan step 7 (cleanup): the retired IssueTagDiscoveryContent.cs held real
    // attorney-drafted interrogatory/RFP language for 14 issue tags beyond the Drainage section
    // step 4 already proved out - content this repo has no git history to recover if it were
    // simply deleted. This ports all 14 into the platform template as real {{#Tag}} sections
    // (exactly the mechanism Drainage already validated) as version 2, activated in place of
    // version 1, the same shape an attorney uploading a new version through the admin screen
    // would produce - runs once per database, guarded by whether the "FullTaking" section already
    // exists on the active version.
    private async Task EnsureInterrogatoriesAllIssueTagSectionsAsync(SqliteConnection connection)
    {
        var active = await GetActiveTemplateVersionAsync(connection, "interrogatories_platform");
        if (active is null) return;

        var alreadyUpgradedCmd = connection.CreateCommand();
        alreadyUpgradedCmd.CommandText = "SELECT COUNT(*) FROM document_template_sections WHERE template_version_id = @v AND section_key = 'FullTaking'";
        alreadyUpgradedCmd.Parameters.AddWithValue("@v", active.Version.Id);
        if (Convert.ToInt32(await alreadyUpgradedCmd.ExecuteScalarAsync()) > 0) return;

        var folder = Path.Combine(_paths.Config.DocumentTemplatesFolder, "platform");
        Directory.CreateDirectory(folder);
        var nextVersion = active.Version.Version + 1;
        var storagePath = Path.Combine(folder, $"interrogatories_platform_v{nextVersion}.docx");
        await File.WriteAllBytesAsync(storagePath, BuildSeedInterrogatoriesDocxV2());

        var now = DateTime.UtcNow.ToString("O");
        var deactivate = connection.CreateCommand();
        deactivate.CommandText = "UPDATE document_template_versions SET is_active = 0 WHERE template_id = @templateId";
        deactivate.Parameters.AddWithValue("@templateId", active.Template.Id);
        await deactivate.ExecuteNonQueryAsync();

        var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = """
            INSERT INTO document_template_versions (template_id, version, storage_path, tokens_json, is_active, created_at)
            VALUES (@templateId, @version, @path, @tokens, 1, @now);
            SELECT last_insert_rowid();
            """;
        versionCmd.Parameters.AddWithValue("@templateId", active.Template.Id);
        versionCmd.Parameters.AddWithValue("@version", nextVersion);
        versionCmd.Parameters.AddWithValue("@path", storagePath);
        versionCmd.Parameters.AddWithValue("@tokens", JsonSerializer.Serialize(new[] { "County", "CaseNumber", "DefendantNames", "AttorneyName" }));
        versionCmd.Parameters.AddWithValue("@now", now);
        var versionId = Convert.ToInt64(await versionCmd.ExecuteScalarAsync());

        var sections = new (string Key, string Label, string Tag)[]
        {
            ("FullTaking", "Full Taking", "Full Taking"),
            ("EasementOnly", "Easement Only", "Easement Only"),
            ("TemporaryConstructionEasement", "Temporary Construction Easement", "Temporary Construction Easement"),
            ("SeveranceDamages", "Severance Damages", "Severance Damages"),
            ("AccessChangeOfAccess", "Access / Change of Access", "Access / Change of Access"),
            ("Drainage", "Drainage", "Drainage"),
            ("LandlockedRemainder", "Landlocked Remainder", "Landlocked Remainder"),
            ("Minerals", "Minerals", "Minerals"),
            ("Timber", "Timber", "Timber"),
            ("BillboardSign", "Billboard / Sign", "Billboard / Sign"),
            ("LeaseholdTenantInterest", "Leasehold / Tenant Interest", "Leasehold / Tenant Interest"),
            ("LienholderMortgage", "Lienholder / Mortgage", "Lienholder / Mortgage"),
            ("EstateProbate", "Estate / Probate", "Estate / Probate"),
            ("UnknownHeirsOwners", "Unknown Heirs / Owners", "Unknown Heirs / Owners"),
            ("UtilityConflict", "Utility Conflict", "Utility Conflict"),
        };
        for (var i = 0; i < sections.Length; i++)
        {
            var (key, label, tag) = sections[i];
            var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO document_template_sections (template_version_id, section_key, label, description, issue_tag_name, sort_order)
                VALUES (@v, @key, @label, @description, @tag, @sort)
                """;
            insert.Parameters.AddWithValue("@v", versionId);
            insert.Parameters.AddWithValue("@key", key);
            insert.Parameters.AddWithValue("@label", label);
            insert.Parameters.AddWithValue("@description", $"Additional interrogatory/request for production for cases tagged '{tag}'.");
            insert.Parameters.AddWithValue("@tag", tag);
            insert.Parameters.AddWithValue("@sort", i + 1);
            await insert.ExecuteNonQueryAsync();
        }
    }

    // Drafted from general eminent-domain practice knowledge, not sourced from the office's actual
    // filings - carried over verbatim from the retired IssueTagDiscoveryContent.cs, where every
    // block was already flagged for attorney review before use on a real case.
    private static byte[] BuildSeedInterrogatoriesDocxV2()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    new Run(new RunProperties(new Bold()), new Text("IN THE CIRCUIT COURT OF {{COUNTY}} COUNTY, ARKANSAS"))),
                new Paragraph(new Run(new RunProperties(new Bold()), new Text("CASE NO. {{CaseNumber}}"))),
                new Paragraph(new Run(new RunProperties(new Bold()), new Text("{{DefendantNames}}"))),
                new Paragraph(),
                new Paragraph(new Run(new RunProperties(new Bold(), new Underline { Val = UnderlineValues.Single }),
                    new Text("INTERROGATORIES AND REQUESTS FOR PRODUCTION"))),
                new Paragraph(),
                SeqInterrogatory("Please state the name, address, and telephone number of the person(s) answering these interrogatories."),
                SeqInterrogatory("Please state what the highest and best use of the property acquired by plaintiff was immediately prior to the acquisition."));

            void AddTagSection(string sectionKey, string[] interrogatories, string[] requestsForProduction)
            {
                body.AppendChild(SectionMarker($"{{{{#{sectionKey}}}}}"));
                foreach (var text in interrogatories) body.AppendChild(SeqInterrogatory(text));
                foreach (var text in requestsForProduction) body.AppendChild(SeqRequestForProduction(text));
                body.AppendChild(SectionMarker($"{{{{/{sectionKey}}}}}"));
            }

            AddTagSection("FullTaking",
                ["State whether you contend any economic unit or portion of the property was left as a usable remainder after Plaintiff's acquisition, and if so, describe it."],
                ["Provide any documents evidencing an ownership interest in property adjoining the subject tract that you contend should be considered in valuing this taking."]);

            AddTagSection("EasementOnly",
                ["State the specific rights you contend are retained by the landowner in the area subject to the permanent easement, and any use you contend is precluded by the easement."],
                ["Provide any documents evidencing prior use of the easement area by the landowner."]);

            AddTagSection("TemporaryConstructionEasement",
                ["State the use, if any, you contend was made of the temporary construction easement area prior to the taking, and the value or rental rate you attribute to that use during the easement period."],
                ["Provide any lease, rental, or income documentation for the temporary construction easement area for the three years preceding the taking."]);

            AddTagSection("SeveranceDamages",
                ["Describe each item of severance damage claimed to the remainder property, the amount attributed to each item, and the factual basis supporting each item.",
                 "State whether any cure or mitigation measure is contended to be necessary to the remainder, and if so, describe the measure and its estimated cost."],
                ["Provide any estimates, bids, or contractor quotes relating to a cure or mitigation measure claimed for the remainder."]);

            AddTagSection("AccessChangeOfAccess",
                ["Describe the access to the property that existed immediately before the taking and the access that exists immediately after, including any change in the number, location, or type of access points.",
                 "State the extent to which you contend the change in access affects the value or highest and best use of the remainder."],
                ["Provide any documents depicting or describing access to the property before and after the taking, including photographs, surveys, or access permits."]);

            body.AppendChild(SectionMarker("{{#Drainage}}"));
            body.AppendChild(SeqInterrogatory("Describe any change in surface water drainage across the property that you contend resulted from the taking or construction, and any resulting damage."));
            body.AppendChild(SeqRequestForProduction("Provide any documents, photographs, or engineering reports relating to drainage conditions on the property before and after construction."));
            body.AppendChild(SectionMarker("{{/Drainage}}"));

            AddTagSection("LandlockedRemainder",
                ["State whether you contend the remainder property lacks legal access to a public road following the taking, and if so, describe the access that existed before the taking and any access presently available."],
                ["Provide any title reports, easement documents, or correspondence relating to access to the remainder property."]);

            AddTagSection("Minerals",
                ["State whether you claim any mineral, oil, gas, or other subsurface interest in the property, and if so, describe the interest and any current or historical production or lease activity."],
                ["Provide any mineral deeds, leases, division orders, or royalty statements relating to the property."]);

            AddTagSection("Timber",
                ["State whether the property contained merchantable timber at the time of the taking, and if so, describe the species, volume, and value attributed to the timber."],
                ["Provide any timber cruise, appraisal, or valuation report relating to timber on the property."]);

            AddTagSection("BillboardSign",
                ["State whether an outdoor advertising structure was located on the property at the time of the taking, and if so, describe the structure, its permit status, and any income generated from it."],
                ["Provide any lease agreements, permits, or income records relating to any outdoor advertising structure on the property."]);

            AddTagSection("LeaseholdTenantInterest",
                ["State whether any portion of the property was subject to a lease at the time of the taking, and if so, identify the tenant and describe the material lease terms, including rent and remaining term."],
                ["Provide a true and correct copy of any lease agreement affecting the property in effect at the time of the taking."]);

            AddTagSection("LienholderMortgage",
                ["Identify each mortgage, deed of trust, or other lien of record against the property at the time of the taking, including the lienholder and the outstanding balance."],
                ["Provide any payoff statement, loan documents, or lien release relating to a mortgage or lien against the property."]);

            AddTagSection("EstateProbate",
                ["State whether the property is or was subject to an open estate or probate proceeding, and if so, identify the court, case number, and personal representative."],
                ["Provide any letters testamentary, letters of administration, or probate court orders relating to the property."]);

            AddTagSection("UnknownHeirsOwners",
                ["State the basis for your knowledge of, or claimed interest in, the property, including how you trace title from the last record owner."],
                ["Provide any documents supporting your claimed heirship or ownership interest in the property, including family records, affidavits of heirship, or probate documents."]);

            AddTagSection("UtilityConflict",
                ["Identify each utility facility located on the property at the time of the taking, including the owner or operator of the facility."],
                ["Provide any utility easements, relocation agreements, or correspondence relating to utility facilities on the property."]);

            body.AppendChild(SeqInterrogatory("Please list each and every appraisal you have obtained for the property, including the name, address, and telephone number of the appraiser and the date of the appraisal."));
            body.AppendChild(new Paragraph());
            body.AppendChild(new Paragraph(new Run(new Text("Respectfully submitted,"))));
            body.AppendChild(new Paragraph(new Run(new Text("{{AttorneyName}}"))));

            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    // ---- Build-plan step 6 (re-author remaining built-ins): Judgment (unified NoTaxes/TaxesOwed
    // branch - the two old JudgmentNoTaxes.txt/JudgmentTaxesOwed.txt files differed only in the
    // tax-disposition paragraphs, exactly the "duplicate files instead of one template with
    // branching" problem the rebuild exists to fix), Settlement Justification Memo, and Requests
    // for Admission. Real attorney-drafted wording, carried over verbatim from the legacy .txt
    // sources (now retired - the platform is docx-only), registered through the same
    // EnsureBuiltinDocumentTemplateAsync path the Interrogatories seed proved out in step 4.
    private async Task EnsureBuiltinDocumentTemplatesAsync(SqliteConnection connection)
    {
        await EnsureBuiltinDocumentTemplateAsync(connection, "judgment_platform",
            "Judgment (Unified Platform)",
            "Final judgment confirming vesting of title and disposition of the deposit. One template covers both the no-taxes-due and taxes-owed variants - toggle whichever applies in the generation checklist.",
            "Judgment", "judgment",
            BuildJudgmentDocx(),
            tokens:
            [
                "County", "CaseNumber", "DefendantNames", "AcquisitionAcres", "Tract", "JobNumber",
                "TractDescription", "TCEDescription", "OwnershipDate", "SummonsDate", "WarningOrderNewspaper",
                "WarningOrderDate1", "WarningOrderDate2", "DepositAmount", "TaxAmount", "JustCompensationAmount",
                "JudgeSignatureDate", "AttorneyName", "BarNumber", "OrgAddressLine1", "OrgAddressLine2",
                "AttorneyPhone", "AttorneyEmail",
            ],
            sections:
            [
                new DocumentTemplateSectionRecord { SectionKey = "NoTaxesOwed", Label = "No Taxes Due", Description = "Tax assessor/collector has no current interest; no taxes owed on the subject property.", SortOrder = 1 },
                new DocumentTemplateSectionRecord { SectionKey = "TaxesOwed", Label = "Taxes Owed", Description = "Current ad valorem taxes are due and paid from the deposit before the balance is disbursed.", SortOrder = 2 },
            ],
            runtimeInputs:
            [
                new DocumentRuntimeInputRecord { FieldKey = "TractDescription", Label = "Tract Legal Description", FieldType = "textarea", IsRequired = true, SortOrder = 1 },
                new DocumentRuntimeInputRecord { FieldKey = "TCEDescription", Label = "Temporary Construction Easement Description (if any)", FieldType = "textarea", IsRequired = false, SortOrder = 2 },
                new DocumentRuntimeInputRecord { FieldKey = "OwnershipDate", Label = "Date Defendant Became Owner", FieldType = "date", IsRequired = true, SortOrder = 3 },
                new DocumentRuntimeInputRecord { FieldKey = "SummonsDate", Label = "Summons Issued Date", FieldType = "date", IsRequired = true, SortOrder = 4 },
                new DocumentRuntimeInputRecord { FieldKey = "WarningOrderNewspaper", Label = "Warning Order Newspaper", FieldType = "text", IsRequired = true, SortOrder = 5 },
                new DocumentRuntimeInputRecord { FieldKey = "WarningOrderDate1", Label = "Warning Order Publication Date 1", FieldType = "date", IsRequired = true, SortOrder = 6 },
                new DocumentRuntimeInputRecord { FieldKey = "WarningOrderDate2", Label = "Warning Order Publication Date 2", FieldType = "date", IsRequired = true, SortOrder = 7 },
                new DocumentRuntimeInputRecord { FieldKey = "JustCompensationAmount", Label = "Just Compensation Amount", FieldType = "number", IsRequired = true, SortOrder = 8 },
                new DocumentRuntimeInputRecord { FieldKey = "JudgeSignatureDate", Label = "Judge Signature Date", FieldType = "date", IsRequired = true, SortOrder = 9 },
            ],
            overlaps: [("NoTaxesOwed", "TaxesOwed", "Mutually exclusive - choose whichever applies to this tract, not both.")]);

        await EnsureBuiltinDocumentTemplateAsync(connection, "settlement_justification_platform",
            "Settlement Justification Memo (Unified Platform)",
            "Inter-office memo recommending settlement, with the before/after appraisal comparison and fee-exposure analysis.",
            "Settlement", "settlement_justification",
            BuildSettlementJustificationDocx(),
            tokens:
            [
                "DivisionHeadName", "RowSectionHeadName", "ChiefLegalCounselName", "AttorneyName", "MemoDate",
                "DefendantNames", "CaseNumber", "JobNumber", "Tract", "ProjectName", "FilingDate",
                "WholePropertyAcres", "PropertyDescription", "AcquisitionAcres", "TCEDescription",
                "OurAppraisalTotal", "OurAppraisalLandBefore", "OurAppraisalPerSfBefore", "OurAppraisalLandAfter",
                "OurAppraisalPerSfAfter", "DefendantAppraisalTotal", "DefendantAppraisalAboveDeposit",
                "DefendantAppraisalLandBefore", "DefendantAppraisalPerSfBefore", "DefendantAppraisalLandAfter",
                "DefendantAppraisalPerSfAfter", "HighestAndBestUse", "ASHCOfferAmount", "ASHCOfferDate",
                "FeeAdjustmentAmount", "CounterofferDate", "CounterofferAmount", "SettlementAmount",
                "TrialFeeLow", "TrialFeeHigh",
            ],
            sections: [],
            runtimeInputs:
            [
                new DocumentRuntimeInputRecord { FieldKey = "MemoDate", Label = "Memo Date", FieldType = "date", IsRequired = true, SortOrder = 1 },
                new DocumentRuntimeInputRecord { FieldKey = "PropertyDescription", Label = "Property Description", FieldType = "textarea", IsRequired = true, SortOrder = 2 },
                new DocumentRuntimeInputRecord { FieldKey = "HighestAndBestUse", Label = "Highest and Best Use", FieldType = "text", IsRequired = true, SortOrder = 3 },
                new DocumentRuntimeInputRecord { FieldKey = "TCEDescription", Label = "Temporary Construction Easement Description", FieldType = "textarea", IsRequired = false, SortOrder = 4 },
                new DocumentRuntimeInputRecord { FieldKey = "OurAppraisalTotal", Label = "Our Appraisal - Total Acquisition Value", FieldType = "number", IsRequired = true, SortOrder = 5 },
                new DocumentRuntimeInputRecord { FieldKey = "OurAppraisalLandBefore", Label = "Our Appraisal - Land Value Before", FieldType = "number", IsRequired = true, SortOrder = 6 },
                new DocumentRuntimeInputRecord { FieldKey = "OurAppraisalPerSfBefore", Label = "Our Appraisal - $/sf Before", FieldType = "number", IsRequired = true, SortOrder = 7 },
                new DocumentRuntimeInputRecord { FieldKey = "OurAppraisalLandAfter", Label = "Our Appraisal - Land Value After", FieldType = "number", IsRequired = true, SortOrder = 8 },
                new DocumentRuntimeInputRecord { FieldKey = "OurAppraisalPerSfAfter", Label = "Our Appraisal - $/sf After", FieldType = "number", IsRequired = true, SortOrder = 9 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalTotal", Label = "Defendant's Appraisal - Total", FieldType = "number", IsRequired = true, SortOrder = 10 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalAboveDeposit", Label = "Defendant's Appraisal - Amount Above Deposit", FieldType = "number", IsRequired = true, SortOrder = 11 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalLandBefore", Label = "Defendant's Appraisal - Land Value Before", FieldType = "number", IsRequired = true, SortOrder = 12 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalPerSfBefore", Label = "Defendant's Appraisal - $/sf Before", FieldType = "number", IsRequired = true, SortOrder = 13 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalLandAfter", Label = "Defendant's Appraisal - Land Value After", FieldType = "number", IsRequired = true, SortOrder = 14 },
                new DocumentRuntimeInputRecord { FieldKey = "DefendantAppraisalPerSfAfter", Label = "Defendant's Appraisal - $/sf After", FieldType = "number", IsRequired = true, SortOrder = 15 },
                new DocumentRuntimeInputRecord { FieldKey = "ASHCOfferAmount", Label = "ASHC Offer Amount", FieldType = "number", IsRequired = true, SortOrder = 16 },
                new DocumentRuntimeInputRecord { FieldKey = "ASHCOfferDate", Label = "ASHC Offer Date", FieldType = "date", IsRequired = true, SortOrder = 17 },
                new DocumentRuntimeInputRecord { FieldKey = "FeeAdjustmentAmount", Label = "Fee Adjustment Amount", FieldType = "number", IsRequired = true, SortOrder = 18 },
                new DocumentRuntimeInputRecord { FieldKey = "CounterofferAmount", Label = "Counteroffer Amount", FieldType = "number", IsRequired = true, SortOrder = 19 },
                new DocumentRuntimeInputRecord { FieldKey = "CounterofferDate", Label = "Counteroffer Date", FieldType = "date", IsRequired = true, SortOrder = 20 },
                new DocumentRuntimeInputRecord { FieldKey = "SettlementAmount", Label = "Settlement Amount", FieldType = "number", IsRequired = true, SortOrder = 21 },
                new DocumentRuntimeInputRecord { FieldKey = "TrialFeeLow", Label = "Estimated Trial Fee Exposure - Low", FieldType = "number", IsRequired = true, SortOrder = 22 },
                new DocumentRuntimeInputRecord { FieldKey = "TrialFeeHigh", Label = "Estimated Trial Fee Exposure - High", FieldType = "number", IsRequired = true, SortOrder = 23 },
            ],
            overlaps: []);

        await EnsureBuiltinDocumentTemplateAsync(connection, "requests_for_admission_platform",
            "Requests for Admission (Unified Platform)",
            "Plaintiff's requests for admission under Ark. R. Civ. P. 36 addressing necessity, just compensation, and waiver of interest/fees/jury trial.",
            "Discovery", "requests_for_admission",
            BuildRequestsForAdmissionDocx(),
            tokens:
            [
                "County", "CaseNumber", "DefendantNames", "AttorneyName", "JobNumber", "ProjectName",
                "JustCompensationAmount", "BarNumber", "OrgAddressLine1", "OrgAddressLine2", "AttorneyPhone",
                "AttorneyEmail", "CertificateDate",
            ],
            sections: [],
            runtimeInputs:
            [
                new DocumentRuntimeInputRecord { FieldKey = "JustCompensationAmount", Label = "Just Compensation Amount", FieldType = "number", IsRequired = true, SortOrder = 1 },
                new DocumentRuntimeInputRecord { FieldKey = "CertificateDate", Label = "Certificate of Service Date", FieldType = "date", IsRequired = true, SortOrder = 2 },
            ],
            overlaps: []);
    }

    // Shared registration path for a built-in template's first version: writes the .docx to the
    // same platform/ storage folder the admin-upload path uses, then inserts template + version +
    // sections + overlaps + runtime inputs in one pass. Idempotent on template_key, same guard as
    // EnsureDocumentPlatformSeedAsync, so re-running InitializeAsync on an existing database is a
    // no-op rather than a duplicate-row error.
    private async Task EnsureBuiltinDocumentTemplateAsync(
        SqliteConnection connection,
        string templateKey,
        string title,
        string description,
        string category,
        string documentType,
        byte[] docxBytes,
        List<string> tokens,
        List<DocumentTemplateSectionRecord> sections,
        List<DocumentRuntimeInputRecord> runtimeInputs,
        List<(string SectionAKey, string SectionBKey, string Note)> overlaps)
    {
        var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM document_templates WHERE template_key = @key";
        countCmd.Parameters.AddWithValue("@key", templateKey);
        if (Convert.ToInt32(await countCmd.ExecuteScalarAsync()) > 0) return;

        var folder = Path.Combine(_paths.Config.DocumentTemplatesFolder, "platform");
        Directory.CreateDirectory(folder);
        var storagePath = Path.Combine(folder, $"{templateKey}_v1.docx");
        await File.WriteAllBytesAsync(storagePath, docxBytes);

        var now = DateTime.UtcNow.ToString("O");
        var templateCmd = connection.CreateCommand();
        templateCmd.CommandText = """
            INSERT INTO document_templates (template_key, title, description, category, document_type, is_builtin, created_at)
            VALUES (@key, @title, @description, @category, @docType, 1, @now);
            SELECT last_insert_rowid();
            """;
        templateCmd.Parameters.AddWithValue("@key", templateKey);
        templateCmd.Parameters.AddWithValue("@title", title);
        templateCmd.Parameters.AddWithValue("@description", description);
        templateCmd.Parameters.AddWithValue("@category", category);
        templateCmd.Parameters.AddWithValue("@docType", documentType);
        templateCmd.Parameters.AddWithValue("@now", now);
        var templateId = Convert.ToInt64(await templateCmd.ExecuteScalarAsync());

        var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = """
            INSERT INTO document_template_versions (template_id, version, storage_path, tokens_json, is_active, created_at)
            VALUES (@templateId, 1, @path, @tokens, 1, @now);
            SELECT last_insert_rowid();
            """;
        versionCmd.Parameters.AddWithValue("@templateId", templateId);
        versionCmd.Parameters.AddWithValue("@path", storagePath);
        versionCmd.Parameters.AddWithValue("@tokens", JsonSerializer.Serialize(tokens));
        versionCmd.Parameters.AddWithValue("@now", now);
        var versionId = Convert.ToInt64(await versionCmd.ExecuteScalarAsync());

        var keyToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO document_template_sections (template_version_id, section_key, label, description, issue_tag_name, sort_order)
                VALUES (@v, @key, @label, @description, @tag, @sort);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("@v", versionId);
            insert.Parameters.AddWithValue("@key", section.SectionKey);
            insert.Parameters.AddWithValue("@label", section.Label);
            insert.Parameters.AddWithValue("@description", (object?)section.Description ?? DBNull.Value);
            insert.Parameters.AddWithValue("@tag", (object?)section.IssueTagName ?? DBNull.Value);
            insert.Parameters.AddWithValue("@sort", section.SortOrder);
            keyToId[section.SectionKey] = Convert.ToInt64(await insert.ExecuteScalarAsync());
        }

        foreach (var overlap in overlaps)
        {
            var idA = keyToId[overlap.SectionAKey];
            var idB = keyToId[overlap.SectionBKey];
            var (smaller, larger) = idA < idB ? (idA, idB) : (idB, idA);
            var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO document_section_overlaps (section_a_id, section_b_id, note) VALUES (@a, @b, @note)";
            insert.Parameters.AddWithValue("@a", smaller);
            insert.Parameters.AddWithValue("@b", larger);
            insert.Parameters.AddWithValue("@note", overlap.Note);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var input in runtimeInputs)
        {
            var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO document_runtime_inputs (template_version_id, field_key, label, field_type, is_required, sort_order)
                VALUES (@v, @key, @label, @type, @required, @sort)
                """;
            insert.Parameters.AddWithValue("@v", versionId);
            insert.Parameters.AddWithValue("@key", input.FieldKey);
            insert.Parameters.AddWithValue("@label", input.Label);
            insert.Parameters.AddWithValue("@type", input.FieldType);
            insert.Parameters.AddWithValue("@required", input.IsRequired ? 1 : 0);
            insert.Parameters.AddWithValue("@sort", input.SortOrder);
            await insert.ExecuteNonQueryAsync();
        }
    }

    // ---- Caption/paragraph helpers shared by the built-in court-document templates ----

    private static Paragraph CaptionLine(string left, string right) => new(
        new ParagraphProperties(new Tabs(new TabStop { Val = TabStopValues.Right, Position = 9360 })),
        new Run(new Text(left)),
        new Run(new TabChar()),
        new Run(new Text(right)));

    private static Paragraph LabeledLine(string label, string value) => new(
        new Run(new Text(label)),
        new Run(new TabChar()),
        new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph CenteredBold(string text) => new(
        new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
        new Run(new RunProperties(new Bold()), new Text(text)));

    private static Paragraph Bold(string text) => new(new Run(new RunProperties(new Bold()), new Text(text)));

    private static Paragraph Plain(string text) => new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph SectionMarker(string marker) => new(new Run(new Text(marker)));

    private static Paragraph Empty() => new();

    private static byte[] BuildJudgmentDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                Plain("IN THE CIRCUIT COURT OF {{County}} COUNTY, ARKANSAS"),
                Plain("CIVIL DIVISION"),
                Empty(),
                CaptionLine("ARKANSAS STATE HIGHWAY COMMISSION", "PLAINTIFF"),
                Empty(),
                CaptionLine("V.", "CASE NO. {{CaseNumber}}"),
                Empty(),
                CaptionLine("{{DefendantNames}}", "DEFENDANTS"),
                Empty(),
                CenteredBold("JUDGMENT"),
                Empty(),
                Plain("On this day comes on for hearing the cause of the Arkansas State Highway Commission, Plaintiff, vs. {{DefendantNames}}, et al., Defendants, on the amount of compensation to be awarded to Defendants for the taking of {{AcquisitionAcres}} acres more or less and any other interest Defendants had which was condemned and previously identified herein as part of Tract {{Tract}}, Job Number {{JobNumber}}, said land being located in {{County}} County, Arkansas, and described in the Complaint and Declaration of Taking as follows:"),
                Empty(),
                Bold("TRACT {{Tract}}:"),
                Empty(),
                Plain("{{TractDescription}}"),
                Empty(),
                Plain("{{TCEDescription}}"),
                Empty(),
                Plain("The Court finds that Defendant {{DefendantNames}} was the legal owner or interest holder of the land described and condemned herein on {{OwnershipDate}}."),
                Plain("The Court finds that the Clerk of this Court issued Summons directed toward Defendants on {{SummonsDate}}. Additionally, a Warning Order was placed in the {{WarningOrderNewspaper}} for two consecutive weeks on {{WarningOrderDate1}}, and {{WarningOrderDate2}}. Defendants were served in the time and manner provided by law."),
                Plain("Plaintiff deposited ${{DepositAmount}} into the Registry of the Court as just compensation. The deposit has not been withdrawn. No parties have answered in this matter."),
                SectionMarker("{{#NoTaxesOwed}}"),
                Plain("The {{County}} County Tax Assessor and Tax Collector have no interest other than assessing and collecting ad valorem taxes due currently or in the future on the subject property. There are no taxes due. Defendants {{County}} County Tax Assessor and Tax Collector should therefore be dismissed from this action."),
                SectionMarker("{{/NoTaxesOwed}}"),
                SectionMarker("{{#TaxesOwed}}"),
                Plain("The {{County}} County Tax Assessor and Collector have no interest other than assessing and collecting ad valorem taxes due currently or in the future on the subject property. There are current taxes due in the amount of ${{TaxAmount}}. Defendants {{County}} County Assessor and Collector should therefore be dismissed from this action after they are paid the amount of ${{TaxAmount}}."),
                SectionMarker("{{/TaxesOwed}}"),
                Plain("The Court finds that, based upon the fact that no other claimants have appeared or claimed any interest herein, such claims, if any there be, should be, and are hereby, cut off and otherwise subordinated for all purposes to the superior claim of Defendants. As a result, this matter is ripe for determination between Plaintiff and Defendants and that such determination will determine the rights of all rightful claimants in these premises."),
                Plain("No one has challenged the estimated just compensation. {{JustCompensationAmount}} dollars (${{JustCompensationAmount}}) constitutes just compensation for the taking of Tract {{Tract}}. The Plaintiff previously deposited with the clerk of the court ${{DepositAmount}} as its estimate of just compensation."),
                Bold("IT IS THEREFORE CONSIDERED, ORDERED, AND ADJUDGED:"),
                SectionMarker("{{#NoTaxesOwed}}"),
                Plain("(a) That Defendants, {{County}} County Tax Assessor and Tax Collector, are hereby dismissed from this action."),
                Plain("(b) Plaintiff is given credit for ${{DepositAmount}} previously deposited into the Registry of the Court as an estimate of just compensation for the subject property, which by the filing of this Judgment shall constitute full and final satisfaction of the Judgment granted herein."),
                Plain("(c) That the funds shall remain in the Registry of the Court until such time as it is claimed by the Defendants herein and if not claimed within one year shall escheat to the State of Arkansas."),
                SectionMarker("{{/NoTaxesOwed}}"),
                SectionMarker("{{#TaxesOwed}}"),
                Plain("(a) That Defendants, {{County}} County Tax Assessor and Tax Collector, are hereby dismissed from this action, after they are paid the amount of ${{TaxAmount}}."),
                Plain("(b) Plaintiff is given credit for ${{DepositAmount}} previously deposited into the Registry of the Court as an estimate of just compensation for the subject property. The Clerk is hereby ordered to issue check in the amount of ${{TaxAmount}} made payable to the {{County}} County Tax Collector and deliver to their office. The filing of this Judgment shall constitute full and final satisfaction of the Judgment granted herein. That the remaining funds shall remain in the Registry of the Court until such time as it is claimed by the Defendants herein and if not claimed within one year shall escheat to the State of Arkansas."),
                SectionMarker("{{/TaxesOwed}}"),
                Plain("IT IS FURTHER CONSIDERED, ORDERED, AND ADJUDGED that the vesting in the Plaintiff of fee simple interest in the land described above and hereinbefore designated in the Complaint and Declaration of Taking as Tract {{Tract}}, Job Number {{JobNumber}}, should be and is hereby confirmed and vested in the Plaintiff."),
                Empty(),
                Empty(),
                Plain("________________________________"),
                Plain("CIRCUIT JUDGE"),
                Empty(),
                Empty(),
                Plain("DATE: {{JudgeSignatureDate}}"),
                Empty(),
                Empty(),
                Bold("APPROVED AS TO FORM AND CONTENT:"),
                Empty(),
                Empty(),
                Empty(),
                Plain("{{AttorneyName}}, ABN {{BarNumber}}"),
                Plain("Attorney for Plaintiff"),
                Plain("Arkansas Dept. of Transportation"),
                Plain("{{OrgAddressLine1}}"),
                Plain("{{OrgAddressLine2}}"),
                Plain("{{AttorneyPhone}}"),
                Plain("{{AttorneyEmail}}"));
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildSettlementJustificationDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                CenteredBold("INTER-OFFICE MEMORANDUM"),
                Empty(),
                LabeledLine("TO:", "{{DivisionHeadName}}, Division Head, Right of Way Division"),
                Plain("Attn: {{RowSectionHeadName}}, Section Head, Administrative Section"),
                Empty(),
                LabeledLine("FROM:", "{{ChiefLegalCounselName}}, Chief Legal Counsel"),
                Plain("By: {{AttorneyName}}, Staff Attorney"),
                Empty(),
                LabeledLine("DATE:", "{{MemoDate}}"),
                Empty(),
                LabeledLine("SUBJECT:", "ASHC v. {{DefendantNames}}, et al."),
                Plain("Case No. {{CaseNumber}}"),
                Plain("Job No. {{JobNumber}}, Tract No. {{Tract}}"),
                Plain("{{ProjectName}}"),
                Empty(),
                Empty(),
                CenteredBold("JUSTIFICATION FOR SETTLEMENT"),
                Empty(),
                Plain("This condemnation was filed on {{FilingDate}}, for the purposes of constructing and maintaining highway facilities on {{ProjectName}}, in connection with Job No. {{JobNumber}}, which involved the acquisition of Tract {{Tract}}. The whole existing property was {{WholePropertyAcres}} acres, more or less. The property is {{PropertyDescription}}. A total of {{AcquisitionAcres}} acres, more or less, was acquired by the ASHC. {{TCEDescription}}"),
                Empty(),
                Plain("ARDOT prepared a before-and-after appraisal that valued the total acquisition at ${{OurAppraisalTotal}}. The land was valued at ${{OurAppraisalLandBefore}} (${{OurAppraisalPerSfBefore}}/sf) before the acquisition and ${{OurAppraisalLandAfter}} (${{OurAppraisalPerSfAfter}}/sf) after the acquisition, based on comparable sales. Defendants obtained an appraisal for the amount of ${{DefendantAppraisalTotal}}, an additional ${{DefendantAppraisalAboveDeposit}} above the initial deposit. Defendants' appraisal also valued the land much higher than the value given by the ARDOT appraisal. The property was valued at ${{DefendantAppraisalLandBefore}} (${{DefendantAppraisalPerSfBefore}}/sf) before the acquisition and ${{DefendantAppraisalLandAfter}} (${{DefendantAppraisalPerSfAfter}}/sf) after the acquisition, based on comparable sales. Both appraisals found that the property had a highest and best use of {{HighestAndBestUse}}."),
                Empty(),
                Plain("Based on the valuation proffered by Defendants' appraisal, a proposal by ASHC of ${{ASHCOfferAmount}} was made on {{ASHCOfferDate}}. An adjustment of ${{FeeAdjustmentAmount}} was made on the initial offer to account for expenses and attorney's fees. As the total amount of ${{ASHCOfferAmount}} does not equal at least 20% above the initial deposit, it would not trigger the automatic statutory award of attorney's fees if such an award were made at trial."),
                Empty(),
                Plain("On {{CounterofferDate}}, Defendants made a counteroffer in the amount of ${{CounterofferAmount}}. Settlement for the sum of ${{SettlementAmount}} is reasonable. Potential risk of taking the matter to trial could result in total fees of ${{TrialFeeLow}}-${{TrialFeeHigh}} on a ${{SettlementAmount}} judgment. This does not include trial costs and fees that would be incurred by the ASHC. I recommend that the offer be accepted and submit the following in support of the recommendation:"),
                Empty(),
                Plain("The cost of further litigation will be avoided by the acceptance of the landowner's offer. The cost of a trial has been estimated by the 2020 Legal Trends Report (updated 2021) to exceed the sum of $20,000.00, plus attorney's fees."),
                Empty(),
                Plain("Ark. Code Ann. § 27-67-317 mandates the awarding of defendants' attorney's fees, costs and appraisal fees when the amount of the judgment exceeds 20% or more of the deposit."),
                Empty(),
                Plain("The amount of the proposed settlement will not bear interest from the date of the taking until just compensation is paid. A jury verdict in excess of the deposit would bear interest from the date of taking until just compensation is paid. See Ark. Code Ann. § 27-67-316(e)."),
                Empty(),
                Empty(),
                Plain("_____________________________"),
                Plain("{{AttorneyName}}"),
                Empty(),
                Empty(),
                Bold("APPROVED:"),
                Plain("{{ChiefLegalCounselName}}, Chief Legal Counsel"));
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static readonly string[] RequestsForAdmissionItems =
    [
        "Admit that, in connection with Job No. {{JobNumber}}, more commonly known as {{ProjectName}}, plans of which are on file at Plaintiff's offices in Little Rock and with the Clerk of this Court, Plaintiff will construct and maintain highway facilities in {{County}} County on the lands described in Plaintiff's Complaint and Declaration of Taking.",
        "Admit that the right of way and construction plans are attached to Plaintiff's Declaration of Taking filed in this cause of action.",
        "Admit that this highway is an uncontrolled access facility.",
        "Admit that Plaintiff's cause of action is justified by traffic conditions at the present time and those contemplated and forecast for the future.",
        "Admit that the tracts of land described in Schedule \"A,\" of Plaintiff's Complaint are located in {{County}} County, Arkansas.",
        "Admit that the tracts of land described in Schedule \"A,\" of Plaintiff's Complaint are needed for highway purposes and for construction and maintenance of uncontrolled access facilities in furtherance of Job No. {{JobNumber}}.",
        "Admit that it is necessary that Plaintiff acquire title to the subject property.",
        "Admit that Plaintiff has already deposited total just compensation (hereinafter \"just compensation\") in the amount of ${{JustCompensationAmount}}, as detailed in its Declaration of Taking, into the registry of this Court.",
        "Admit that just compensation in this cause of action is exactly equal to the amount Plaintiff previously deposited into the registry of this Court, or ${{JustCompensationAmount}}.",
        "Admit that just compensation in the amount of ${{JustCompensationAmount}} is sufficient to compensate Defendant in total for the lands acquired by Plaintiff in this cause of action.",
        "Admit that ${{JustCompensationAmount}} represents the fair market value of the lands acquired by Plaintiff in this cause of action.",
        "Admit that the amount of just compensation deposited by Plaintiff makes Defendant whole for lands acquired by Plaintiff.",
        "Admit that Defendant is not entitled to any compensation in excess of the just compensation previously deposited by Plaintiff in the amount of ${{JustCompensationAmount}}.",
        "Admit that Defendant is not entitled to interest on the just compensation previously deposited by Plaintiff, or that Defendant waives interest on the just compensation previously deposited.",
        "Admit that Defendant is not entitled to pre-judgment interest.",
        "Admit that Defendant waives pre-judgment interest on the just compensation previously deposited.",
        "Admit that Defendant is not entitled to post-judgment interest.",
        "Admit that Defendant waives post-judgment interest on the just compensation previously deposited.",
        "Admit that Defendant is not entitled to an award of attorneys' fees, expenses, and costs, in this cause of action.",
        "Admit that Defendant waives an award of attorneys' fees, expenses, and costs, in this cause of action.",
        "Admit that Defendant is not entitled to an award of damages.",
        "Admit that Defendant waives an award of damages in this cause of action.",
        "Admit that Defendant had the right to hire an appraiser or other independent professional to determine the value of Defendant's private property or to assist Defendant in this condemnation proceeding.",
        "Admit that Defendant has elected not to exercise the right to hire an appraiser or other independent professional to determine the value of Defendant's private property or to assist Defendant in this condemnation proceeding.",
        "Admit that by admitting to the Requests for Admissions herein, there is no genuine issue of material fact to present to the jury for determination at trial between Plaintiff and Defendant.",
        "Admit that by admitting to the Requests for Admissions herein, Defendant hereby waives Defendant's right to a trial by jury pursuant to either Ark. Code Ann. §§ 18-15-103 and 27-67-316.",
    ];

    private static byte[] BuildRequestsForAdmissionDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body(
                Plain("IN THE CIRCUIT COURT OF {{County}} COUNTY, ARKANSAS"),
                Empty(),
                CaptionLine("ARKANSAS STATE HIGHWAY COMMISSION", "PLAINTIFF"),
                Empty(),
                CaptionLine("V.", "CASE NO. {{CaseNumber}}"),
                Empty(),
                CaptionLine("{{DefendantNames}}", "DEFENDANTS"),
                Empty(),
                CenteredBold("REQUESTS FOR ADMISSION"),
                Empty(),
                Plain("Plaintiff, Arkansas State Highway Commission, by and through counsel, {{AttorneyName}}, pursuant to Ark. R. Civ. P. 36, propounds the following Requests for Admission upon Separate Defendant, {{DefendantNames}}:"),
                Empty());

            for (var i = 0; i < RequestsForAdmissionItems.Length; i++)
            {
                body.AppendChild(new Paragraph(
                    new Run(new RunProperties(new Bold()), new Text($"REQUEST FOR ADMISSION NO. {i + 1}: ")),
                    new Run(new Text(RequestsForAdmissionItems[i]) { Space = SpaceProcessingModeValues.Preserve })));
            }

            body.AppendChild(Empty());
            body.AppendChild(Plain("Respectfully submitted,"));
            body.AppendChild(Plain("Arkansas State Highway Commission"));
            body.AppendChild(Empty());
            body.AppendChild(Empty());
            body.AppendChild(Plain("By: ________________________________"));
            body.AppendChild(Plain("{{AttorneyName}}"));
            body.AppendChild(Plain("Staff Attorney (Ark. Bar # {{BarNumber}})"));
            body.AppendChild(Plain("Arkansas State Highway Commission"));
            body.AppendChild(Plain("{{OrgAddressLine1}}"));
            body.AppendChild(Plain("{{OrgAddressLine2}}"));
            body.AppendChild(Plain("{{AttorneyPhone}}"));
            body.AppendChild(Plain("{{AttorneyEmail}}"));
            body.AppendChild(Empty());
            body.AppendChild(CenteredBold("CERTIFICATE OF SERVICE"));
            body.AppendChild(Empty());
            body.AppendChild(Plain("I, {{AttorneyName}}, hereby certify that on {{CertificateDate}}, a true and correct copy of the foregoing was submitted via the eFlex filing system and a courtesy copy was sent via email to the following:"));
            body.AppendChild(Empty());
            body.AppendChild(Empty());
            body.AppendChild(Plain("{{AttorneyName}}"));

            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    // ---- Build-plan step 5 (unified Settings UI): Document Templates admin ----

    private static async Task<List<DocumentSectionOverlapPair>> GetOverlapPairsAsync(SqliteConnection connection, List<DocumentTemplateSectionRecord> sections)
    {
        var pairs = new List<DocumentSectionOverlapPair>();
        if (sections.Count == 0) return pairs;

        var idToKey = sections.ToDictionary(s => s.Id, s => s.SectionKey);
        var ids = sections.Select(s => s.Id).ToList();
        var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT section_a_id, section_b_id, note FROM document_section_overlaps WHERE section_a_id IN ({placeholders}) OR section_b_id IN ({placeholders})";
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@id{i}", ids[i]);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var a = reader.GetInt64(0);
            var b = reader.GetInt64(1);
            if (!idToKey.TryGetValue(a, out var keyA) || !idToKey.TryGetValue(b, out var keyB)) continue;
            pairs.Add(new DocumentSectionOverlapPair { SectionAKey = keyA, SectionBKey = keyB, Note = reader.IsDBNull(2) ? null : reader.GetString(2) });
        }

        return pairs;
    }

    private static async Task<DocumentTemplateAdminSummary> GetDocumentTemplateAdminSummaryAsync(SqliteConnection connection, string templateKey, IReadOnlyList<string>? lintIssues = null)
    {
        var templateCmd = connection.CreateCommand();
        templateCmd.CommandText = """
            SELECT id, template_key, title, description, category, document_type, is_builtin, created_at, created_by
            FROM document_templates WHERE template_key = @key AND is_deleted = 0
            """;
        templateCmd.Parameters.AddWithValue("@key", templateKey);
        DocumentTemplateRecord template;
        await using (var reader = await templateCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException($"Template '{templateKey}' not found.");
            }

            template = new DocumentTemplateRecord
            {
                Id = reader.GetInt64(0),
                TemplateKey = reader.GetString(1),
                Title = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                Category = reader.GetString(4),
                DocumentType = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsBuiltin = reader.GetInt64(6) == 1,
                CreatedAt = reader.GetString(7),
                CreatedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
            };
        }

        var versions = new List<DocumentTemplateVersionRecord>();
        var versionsCmd = connection.CreateCommand();
        versionsCmd.CommandText = """
            SELECT id, version, storage_path, tokens_json, unknown_tokens_json, is_active, created_at, created_by
            FROM document_template_versions WHERE template_id = @id ORDER BY version DESC
            """;
        versionsCmd.Parameters.AddWithValue("@id", template.Id);
        await using (var reader = await versionsCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                versions.Add(new DocumentTemplateVersionRecord
                {
                    Id = reader.GetInt64(0),
                    TemplateId = template.Id,
                    Version = reader.GetInt32(1),
                    StoragePath = reader.GetString(2),
                    Tokens = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                    UnknownTokens = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [],
                    IsActive = reader.GetInt64(5) == 1,
                    CreatedAt = reader.GetString(6),
                    CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            }
        }

        var activeVersion = versions.FirstOrDefault(v => v.IsActive);
        var sections = activeVersion is null ? [] : await GetTemplateSectionsAsync(connection, activeVersion.Id);
        var runtimeInputs = activeVersion is null ? [] : await GetRuntimeInputsAsync(connection, activeVersion.Id);
        var overlaps = await GetOverlapPairsAsync(connection, sections);

        return new DocumentTemplateAdminSummary
        {
            Template = template,
            ActiveVersion = activeVersion,
            Versions = versions,
            Sections = sections,
            Overlaps = overlaps,
            RuntimeInputs = runtimeInputs,
            LintIssues = lintIssues?.ToList() ?? [],
        };
    }

    public async Task<List<DocumentTemplateAdminSummary>> GetAllDocumentTemplatesForAdminAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var keys = new List<string>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT template_key FROM document_templates WHERE is_deleted = 0 ORDER BY category, title";
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
        }

        var summaries = new List<DocumentTemplateAdminSummary>();
        foreach (var key in keys) summaries.Add(await GetDocumentTemplateAdminSummaryAsync(connection, key));
        return summaries;
    }

    // Structural section problems (unbalanced/stray/nested markers) would make DocxSectionMerger
    // throw at generation time - much the worse moment to discover them - so those specific linter
    // issues block the upload outright. Unknown-field and stray-brace issues stay advisory: at
    // upload time the template's runtime inputs haven't been declared yet, so a field that looks
    // "unknown" here may just not have its manifest entry yet.
    private static readonly string[] BlockingLintKeywords = ["never closed", "never opened", "doesn't match", "nested"];

    public async Task<DocumentTemplateAdminSummary> UploadDocumentTemplateAsync(string templateKey, string title, string? description, string category, byte[] fileBytes)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            throw new ArgumentException("A template key is required.", nameof(templateKey));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A title is required.", nameof(title));
        }

        var lintIssues = DocxTemplateLinter.Validate(fileBytes);
        var blocking = lintIssues.Where(issue => BlockingLintKeywords.Any(keyword => issue.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
        if (blocking.Count > 0)
        {
            throw new InvalidOperationException("Fix these section problems before uploading: " + string.Join(" ", blocking));
        }

        var tokens = DocumentGenerationEngine.ExtractTokensFromDocx(fileBytes);

        return await WithWriteAsync(async (connection, tx) =>
        {
            var templateCmd = connection.CreateCommand();
            templateCmd.Transaction = tx;
            templateCmd.CommandText = "SELECT id, is_builtin FROM document_templates WHERE template_key = @key AND is_deleted = 0";
            templateCmd.Parameters.AddWithValue("@key", templateKey);

            long templateId;
            var now = DateTime.UtcNow.ToString("O");
            await using (var reader = await templateCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    if (reader.GetInt64(1) == 1)
                    {
                        throw new InvalidOperationException("This template key belongs to a built-in template and can't be replaced this way.");
                    }

                    templateId = reader.GetInt64(0);
                }
                else
                {
                    templateId = -1;
                }
            }

            if (templateId == -1)
            {
                var insertTemplate = connection.CreateCommand();
                insertTemplate.Transaction = tx;
                insertTemplate.CommandText = """
                    INSERT INTO document_templates (template_key, title, description, category, is_builtin, created_at, created_by)
                    VALUES (@key, @title, @description, @category, 0, @now, @by);
                    SELECT last_insert_rowid();
                    """;
                insertTemplate.Parameters.AddWithValue("@key", templateKey);
                insertTemplate.Parameters.AddWithValue("@title", title);
                insertTemplate.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
                insertTemplate.Parameters.AddWithValue("@category", string.IsNullOrWhiteSpace(category) ? "Other" : category);
                insertTemplate.Parameters.AddWithValue("@now", now);
                insertTemplate.Parameters.AddWithValue("@by", _actor.AuditLabel);
                templateId = Convert.ToInt64(await insertTemplate.ExecuteScalarAsync());
            }

            var versionCmd = connection.CreateCommand();
            versionCmd.Transaction = tx;
            versionCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM document_template_versions WHERE template_id = @templateId";
            versionCmd.Parameters.AddWithValue("@templateId", templateId);
            var nextVersion = Convert.ToInt32(await versionCmd.ExecuteScalarAsync()) + 1;

            var folder = Path.Combine(_paths.Config.DocumentTemplatesFolder, "platform");
            Directory.CreateDirectory(folder);
            var storagePath = Path.Combine(folder, $"{templateKey}_v{nextVersion}.docx");
            await File.WriteAllBytesAsync(storagePath, fileBytes);

            var deactivate = connection.CreateCommand();
            deactivate.Transaction = tx;
            deactivate.CommandText = "UPDATE document_template_versions SET is_active = 0 WHERE template_id = @templateId";
            deactivate.Parameters.AddWithValue("@templateId", templateId);
            await deactivate.ExecuteNonQueryAsync();

            var insertVersion = connection.CreateCommand();
            insertVersion.Transaction = tx;
            insertVersion.CommandText = """
                INSERT INTO document_template_versions (template_id, version, storage_path, tokens_json, is_active, created_at, created_by)
                VALUES (@templateId, @version, @path, @tokens, 1, @now, @by)
                """;
            insertVersion.Parameters.AddWithValue("@templateId", templateId);
            insertVersion.Parameters.AddWithValue("@version", nextVersion);
            insertVersion.Parameters.AddWithValue("@path", storagePath);
            insertVersion.Parameters.AddWithValue("@tokens", JsonSerializer.Serialize(tokens));
            insertVersion.Parameters.AddWithValue("@now", now);
            insertVersion.Parameters.AddWithValue("@by", _actor.AuditLabel);
            await insertVersion.ExecuteNonQueryAsync();

            return await GetDocumentTemplateAdminSummaryAsync(connection, templateKey, lintIssues);
        });
    }

    // Replace-all for a version's configuration in one transaction - simpler and safer for a small
    // admin list an attorney configures occasionally than granular per-item CRUD endpoints.
    public async Task<DocumentTemplateAdminSummary> SaveDocumentTemplateConfigurationAsync(string templateKey, DocumentTemplateConfigurationRequest request)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var active = await GetActiveTemplateVersionAsync(connection, templateKey)
                ?? throw new InvalidOperationException($"No active version found for template '{templateKey}'.");

            var deleteOverlaps = connection.CreateCommand();
            deleteOverlaps.Transaction = tx;
            deleteOverlaps.CommandText = """
                DELETE FROM document_section_overlaps WHERE section_a_id IN (SELECT id FROM document_template_sections WHERE template_version_id = @v)
                                                          OR section_b_id IN (SELECT id FROM document_template_sections WHERE template_version_id = @v)
                """;
            deleteOverlaps.Parameters.AddWithValue("@v", active.Version.Id);
            await deleteOverlaps.ExecuteNonQueryAsync();

            var deleteSections = connection.CreateCommand();
            deleteSections.Transaction = tx;
            deleteSections.CommandText = "DELETE FROM document_template_sections WHERE template_version_id = @v";
            deleteSections.Parameters.AddWithValue("@v", active.Version.Id);
            await deleteSections.ExecuteNonQueryAsync();

            var deleteInputs = connection.CreateCommand();
            deleteInputs.Transaction = tx;
            deleteInputs.CommandText = "DELETE FROM document_runtime_inputs WHERE template_version_id = @v";
            deleteInputs.Parameters.AddWithValue("@v", active.Version.Id);
            await deleteInputs.ExecuteNonQueryAsync();

            var keyToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in request.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.SectionKey) || string.IsNullOrWhiteSpace(section.Label))
                {
                    throw new InvalidOperationException("Every section needs a key and a label.");
                }

                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO document_template_sections (template_version_id, section_key, label, description, issue_tag_name, sort_order)
                    VALUES (@v, @key, @label, @description, @tag, @sort);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("@v", active.Version.Id);
                insert.Parameters.AddWithValue("@key", section.SectionKey);
                insert.Parameters.AddWithValue("@label", section.Label);
                insert.Parameters.AddWithValue("@description", (object?)section.Description ?? DBNull.Value);
                insert.Parameters.AddWithValue("@tag", (object?)section.IssueTagName ?? DBNull.Value);
                insert.Parameters.AddWithValue("@sort", section.SortOrder);
                keyToId[section.SectionKey] = Convert.ToInt64(await insert.ExecuteScalarAsync());
            }

            foreach (var overlap in request.Overlaps)
            {
                if (!keyToId.TryGetValue(overlap.SectionAKey, out var idA) || !keyToId.TryGetValue(overlap.SectionBKey, out var idB))
                {
                    throw new InvalidOperationException($"Overlap pair references a section that isn't in this configuration: '{overlap.SectionAKey}' / '{overlap.SectionBKey}'.");
                }

                if (idA == idB)
                {
                    throw new InvalidOperationException("A section can't overlap with itself.");
                }

                var (smaller, larger) = idA < idB ? (idA, idB) : (idB, idA);
                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO document_section_overlaps (section_a_id, section_b_id, note) VALUES (@a, @b, @note)";
                insert.Parameters.AddWithValue("@a", smaller);
                insert.Parameters.AddWithValue("@b", larger);
                insert.Parameters.AddWithValue("@note", (object?)overlap.Note ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync();
            }

            foreach (var input in request.RuntimeInputs)
            {
                if (string.IsNullOrWhiteSpace(input.FieldKey) || string.IsNullOrWhiteSpace(input.Label))
                {
                    throw new InvalidOperationException("Every runtime input needs a field key and a label.");
                }

                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO document_runtime_inputs (template_version_id, field_key, label, field_type, is_required, sort_order)
                    VALUES (@v, @key, @label, @type, @required, @sort)
                    """;
                insert.Parameters.AddWithValue("@v", active.Version.Id);
                insert.Parameters.AddWithValue("@key", input.FieldKey);
                insert.Parameters.AddWithValue("@label", input.Label);
                insert.Parameters.AddWithValue("@type", string.IsNullOrWhiteSpace(input.FieldType) ? "text" : input.FieldType);
                insert.Parameters.AddWithValue("@required", input.IsRequired ? 1 : 0);
                insert.Parameters.AddWithValue("@sort", input.SortOrder);
                await insert.ExecuteNonQueryAsync();
            }

            return await GetDocumentTemplateAdminSummaryAsync(connection, templateKey);
        });
    }

    public async Task<DocumentTemplateAdminSummary> ActivateDocumentTemplateVersionAsync(string templateKey, int version)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var templateCmd = connection.CreateCommand();
            templateCmd.Transaction = tx;
            templateCmd.CommandText = "SELECT id FROM document_templates WHERE template_key = @key AND is_deleted = 0";
            templateCmd.Parameters.AddWithValue("@key", templateKey);
            var templateIdObj = await templateCmd.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Template '{templateKey}' not found.");
            var templateId = Convert.ToInt64(templateIdObj);

            var deactivate = connection.CreateCommand();
            deactivate.Transaction = tx;
            deactivate.CommandText = "UPDATE document_template_versions SET is_active = 0 WHERE template_id = @id";
            deactivate.Parameters.AddWithValue("@id", templateId);
            await deactivate.ExecuteNonQueryAsync();

            var activate = connection.CreateCommand();
            activate.Transaction = tx;
            activate.CommandText = "UPDATE document_template_versions SET is_active = 1 WHERE template_id = @id AND version = @version";
            activate.Parameters.AddWithValue("@id", templateId);
            activate.Parameters.AddWithValue("@version", version);
            if (await activate.ExecuteNonQueryAsync() == 0)
            {
                throw new InvalidOperationException($"Version {version} not found for template '{templateKey}'.");
            }

            return await GetDocumentTemplateAdminSummaryAsync(connection, templateKey);
        });
    }

    public async Task DeleteDocumentTemplateAsync(string templateKey)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT is_builtin FROM document_templates WHERE template_key = @key AND is_deleted = 0";
            cmd.Parameters.AddWithValue("@key", templateKey);
            var result = await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Template '{templateKey}' not found.");
            if (Convert.ToInt64(result) == 1)
            {
                throw new InvalidOperationException("Built-in templates can't be deleted.");
            }

            var delete = connection.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "UPDATE document_templates SET is_deleted = 1 WHERE template_key = @key";
            delete.Parameters.AddWithValue("@key", templateKey);
            await delete.ExecuteNonQueryAsync();
            return 0;
        });
    }
}
