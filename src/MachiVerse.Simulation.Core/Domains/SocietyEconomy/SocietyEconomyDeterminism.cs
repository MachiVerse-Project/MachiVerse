using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public sealed record ProductionMaterialV1(StableToken Material, long Quantity)
{
    public void Validate()
    {
        if (Quantity <= 0) throw new InvalidDataException("society.production.quantity-nonpositive");
    }
}

public sealed class ProductionRecipeV1
{
    public ProductionRecipeV1(
        StableToken recipeId,
        IEnumerable<ProductionMaterialV1> inputs,
        IEnumerable<ProductionMaterialV1> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        RecipeId = recipeId;
        Inputs = Canonicalize(inputs, "society.production.input-duplicate");
        Outputs = Canonicalize(outputs, "society.production.output-duplicate");
        if (Inputs.Count == 0 || Outputs.Count == 0)
            throw new InvalidDataException("society.production.recipe-empty-side");
    }

    public StableToken RecipeId { get; }
    public IReadOnlyList<ProductionMaterialV1> Inputs { get; }
    public IReadOnlyList<ProductionMaterialV1> Outputs { get; }

    private static IReadOnlyList<ProductionMaterialV1> Canonicalize(
        IEnumerable<ProductionMaterialV1> materials,
        string duplicateCode)
    {
        var canonical = materials
            .Select(material =>
            {
                ArgumentNullException.ThrowIfNull(material);
                material.Validate();
                return material;
            })
            .OrderBy(static material => material.Material.Value, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(static material => material.Material.Value)
            .Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new InvalidDataException(duplicateCode);
        return Array.AsReadOnly(canonical);
    }
}

public static class DeterministicProductionV1
{
    public static IReadOnlyDictionary<StableToken, long> Apply(
        IReadOnlyDictionary<StableToken, long> stock,
        ProductionRecipeV1 recipe,
        long batches)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(recipe);
        if (batches <= 0) throw new InvalidDataException("society.production.batch-nonpositive");

        var next = new SortedDictionary<StableToken, long>(stock);
        foreach (var pair in next)
        {
            if (pair.Value < 0) throw new InvalidDataException("society.production.stock-negative");
        }

        try
        {
            foreach (var input in recipe.Inputs)
            {
                var required = checked(input.Quantity * batches);
                var available = next.GetValueOrDefault(input.Material, 0);
                if (available < required)
                    throw new InvalidDataException("society.production.insufficient-input");
                next[input.Material] = checked(available - required);
            }
            foreach (var output in recipe.Outputs)
            {
                var produced = checked(output.Quantity * batches);
                next[output.Material] = checked(next.GetValueOrDefault(output.Material, 0) + produced);
            }
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }

        return next;
    }
}

public sealed record SocietyPropertyRightV1(
    OpaqueId128 RightId,
    OpaqueId128 SubjectId,
    OpaqueId128 HolderId,
    long Quantity)
{
    public void Validate()
    {
        if (RightId.IsZero || SubjectId.IsZero || HolderId.IsZero)
            throw new InvalidDataException("society.property-id-zero");
        if (Quantity <= 0) throw new InvalidDataException("society.property-quantity-nonpositive");
    }

    public SocietyPropertyRightV1 TransferTo(OpaqueId128 newHolderId)
    {
        Validate();
        if (newHolderId.IsZero) throw new InvalidDataException("society.property-holder-zero");
        return this with { HolderId = newHolderId };
    }
}
