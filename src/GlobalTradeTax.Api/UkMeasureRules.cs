using System.Text.Json;

namespace GlobalTradeTax.Api;

// Typed reference graph only. Conditions and descriptions are never interpreted as legal clearance.
public sealed record UkRuleComponent(string Id, string? ExpressionCode, decimal? Amount, string? Currency,
    string? Unit, string? Qualifier, bool Complete);
public sealed record UkRuleCondition(string Id, string? ConditionCode, string? ActionCode,
    string? DocumentCode, string? Description, bool Complete);
public sealed record UkConditionAlternatives(string Id, string? ConditionCode, string[][] Options, bool Complete);
public sealed record UkMeasureRule(string MeasureId, string? MeasureTypeId, string? Description, bool ImportLinked,
    bool Vat, bool Excise, string? RawExpression, UkRuleComponent[] Components, UkRuleCondition[] Conditions,
    UkConditionAlternatives[] ConditionAlternatives, string[] QuotaIds, string[] AdditionalCodeIds,
    string? PreferenceCode, string[] LegalActIds, string[] FootnoteIds, bool Complete, string[] Checks);
public sealed record UkRuleReport(UkMeasureRule[] Measures, string[] Warnings, bool Complete);

public static class UkMeasureRules
{
    public static UkRuleReport Read(UkReferenceResult source, string code)
    {
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        if (source.Status != "reference_only" || source.Data is not { } root || !UkJsonApiContract.IsValidCommodity(root, code))
            return new([], ["commodity_contract_unverified"], false);
        var resources = new Dictionary<(string Type, string Id), JsonElement>();
        resources.Add(("commodity", root.GetProperty("data").GetProperty("id").GetString()!), root.GetProperty("data"));
        if (!root.TryGetProperty("included", out var included)) return new([], ["included_resources_missing"], false);
        if (included.GetArrayLength() > 4096) return new([], ["reference_resource_budget_exceeded"], false);
        foreach (var item in included.EnumerateArray())
        {
            var key = (UkEvidenceJson.String(item, "type")!, UkEvidenceJson.String(item, "id")!);
            if (!resources.TryAdd(key, item)) warnings.Add("duplicate_reference_resource:" + key.Item1 + ":" + key.Item2);
        }
        var primary = root.GetProperty("data");
        if (!UkEvidenceJson.TryReferences(primary, "import_measures", "measure", true, out var imports))
            warnings.Add("import_measure_linkage_unverified");
        var ids = resources.Keys.Where(x => x.Type == "measure").Select(x => x.Id).Concat(imports)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length > 200) warnings.Add("measure_rule_budget_exceeded_incomplete_evidence");
        var rules = new List<UkMeasureRule>();
        foreach (var id in ids.Take(200))
        {
            var checks = new HashSet<string>(StringComparer.Ordinal);
            if (!resources.TryGetValue(("measure", id), out var measure))
            {
                warnings.Add("import_measure_resource_missing:" + id);
                rules.Add(new(id, null, null, imports.Contains(id, StringComparer.Ordinal), false, false, null,
                    [], [], [], [], [], null, [], [], false, ["measure_resource_missing"]));
                continue;
            }
            var attrs = UkEvidenceJson.Object(measure, "attributes");
            JsonElement Linked(JsonElement resource, string relation, string type, bool optional = false)
            {
                var relations = UkEvidenceJson.Object(resource, "relationships");
                var relationship = UkEvidenceJson.Object(relations, relation);
                if (optional && (relationship.ValueKind == JsonValueKind.Undefined ||
                    relationship.TryGetProperty("data", out var empty) && empty.ValueKind == JsonValueKind.Null)) return default;
                if (!UkEvidenceJson.TryReferences(resource, relation, type, false, out var refs) ||
                    !resources.TryGetValue((type, refs[0]), out var linked))
                { checks.Add("linked_resource_unverified:" + relation); return default; }
                return linked;
            }
            string[] Many(JsonElement resource, string relation, string type, bool optional = false)
            {
                var rel = UkEvidenceJson.Object(resource, "relationships");
                if (optional && (rel.ValueKind == JsonValueKind.Undefined || !rel.TryGetProperty(relation, out var link) ||
                    link.ValueKind == JsonValueKind.Object && link.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Null)) return [];
                if (!UkEvidenceJson.TryReferences(resource, relation, type, true, out var refs))
                { checks.Add("relationship_unverified:" + relation); return []; }
                foreach (var reference in refs) if (!resources.ContainsKey((type, reference))) checks.Add("linked_resource_missing:" + type + ":" + reference);
                return refs;
            }
            string[] OptionalOneIds(string relation, string type)
            {
                _ = Linked(measure, relation, type, true);
                // Preserve the declaration even when its linked resource is absent.
                return UkEvidenceJson.TryReferences(measure, relation, type, false, out var refs) ? refs : [];
            }
            var typeResource = Linked(measure, "measure_type", "measure_type");
            var typeAttrs = UkEvidenceJson.Object(typeResource, "attributes");
            var expression = Linked(measure, "duty_expression", "duty_expression");
            var components = new List<UkRuleComponent>();
            foreach (var componentId in Many(measure, "measure_components", "measure_component"))
            {
                if (!resources.TryGetValue(("measure_component", componentId), out var raw)) continue;
                var a = UkEvidenceJson.Object(raw, "attributes");
                var expressionCode = UkEvidenceJson.String(a, "duty_expression_id");
                var currencyOk = NullableText(a, "monetary_unit_code", out var currency);
                var unitOk = NullableText(a, "measurement_unit_code", out var unit);
                var qualifierOk = NullableText(a, "measurement_unit_qualifier_code", out var qualifier);
                var amountOk = Amount(a, "duty_amount", out var amount);
                var complete = expressionCode is not null && currencyOk && unitOk && qualifierOk && amountOk;
                if (unit is not null)
                {
                    var linkedUnit = Linked(raw, "measurement_unit", "measurement_unit");
                    complete &= UkEvidenceJson.String(linkedUnit, "id") == unit;
                }
                if (qualifier is not null)
                {
                    var linkedQualifier = Linked(raw, "measurement_unit_qualifier", "measurement_unit_qualifier");
                    complete &= UkEvidenceJson.String(linkedQualifier, "id") == qualifier;
                }
                if (!complete) checks.Add("component_contract_unverified:" + componentId);
                components.Add(new(componentId, expressionCode, amount, currency, unit, qualifier, complete));
            }
            var conditionIds = Many(measure, "measure_conditions", "measure_condition");
            var conditions = new List<UkRuleCondition>();
            foreach (var conditionId in conditionIds)
            {
                if (!resources.TryGetValue(("measure_condition", conditionId), out var raw)) continue;
                var a = UkEvidenceJson.Object(raw, "attributes");
                var condition = UkEvidenceJson.String(a, "condition_code");
                var action = UkEvidenceJson.String(a, "action_code");
                var complete = condition is not null && action is not null;
                if (!complete) checks.Add("condition_contract_unverified:" + conditionId);
                conditions.Add(new(conditionId, condition, action, UkEvidenceJson.String(a, "document_code"),
                    UkEvidenceJson.String(a, "certificate_description") ?? UkEvidenceJson.String(a, "action"), complete));
            }
            var alternatives = new List<UkConditionAlternatives>();
            var groupIds = Many(measure, "measure_condition_permutation_groups", "measure_condition_permutation_group", true);
            if (conditions.Count > 0 && groupIds.Length == 0) checks.Add("condition_alternatives_not_resolved");
            foreach (var groupId in groupIds)
            {
                if (!resources.TryGetValue(("measure_condition_permutation_group", groupId), out var group)) continue;
                var options = new List<string[]>();
                var complete = true;
                foreach (var permutationId in Many(group, "permutations", "measure_condition_permutation"))
                {
                    if (!resources.TryGetValue(("measure_condition_permutation", permutationId), out var permutation)) { complete = false; continue; }
                    var option = Many(permutation, "measure_conditions", "measure_condition");
                    complete &= option.Length > 0 && option.All(x => conditionIds.Contains(x, StringComparer.Ordinal));
                    options.Add(option);
                }
                complete &= options.Count > 0;
                if (!complete) checks.Add("condition_alternatives_incomplete:" + groupId);
                alternatives.Add(new(groupId, UkEvidenceJson.String(UkEvidenceJson.Object(group, "attributes"), "condition_code"), options.ToArray(), complete));
            }
            var quotas = OptionalOneIds("order_number", "order_number");
            var additional = OptionalOneIds("additional_code", "additional_code");
            var preference = Linked(measure, "preference_code", "preference_code", true);
            var acts = Many(measure, "legal_acts", "legal_act");
            var footnotes = Many(measure, "footnotes", "footnote");
            bool Flag(string name)
            {
                if (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty(name, out var flag) &&
                    flag.ValueKind is JsonValueKind.True or JsonValueKind.False) return flag.GetBoolean();
                checks.Add("measure_flag_unverified:" + name);
                return false;
            }
            var isVat = Flag("vat");
            var isExcise = Flag("excise");
            rules.Add(new(id, UkEvidenceJson.String(typeResource, "id"), UkEvidenceJson.String(typeAttrs, "description"),
                imports.Contains(id, StringComparer.Ordinal), isVat, isExcise,
                UkEvidenceJson.String(UkEvidenceJson.Object(expression, "attributes"), "base"), components.ToArray(), conditions.ToArray(),
                alternatives.ToArray(), quotas, additional, UkEvidenceJson.String(UkEvidenceJson.Object(preference, "attributes"), "code"),
                acts, footnotes, checks.Count == 0 && warnings.Count == 0, checks.Order(StringComparer.Ordinal).ToArray()));
        }
        return new(rules.ToArray(), warnings.Order(StringComparer.Ordinal).ToArray(), warnings.Count == 0 && rules.All(x => x.Complete));
    }

    private static bool NullableText(JsonElement parent, string name, out string? text)
    {
        text = null;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return false;
        text = value.GetString(); return true;
    }
    private static bool Amount(JsonElement parent, string name, out decimal? amount)
    {
        amount = null;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Number) return false;
        var text = value.GetRawText();
        if (text.Contains('e', StringComparison.OrdinalIgnoreCase) || text.Count(char.IsAsciiDigit) > 28 ||
            !value.TryGetDecimal(out var parsed) || parsed < 0) return false;
        amount = parsed; return true;
    }
}
