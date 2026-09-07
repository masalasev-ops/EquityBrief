namespace EquityBrief.Data.Migrations;

// One schema change. A migration that has run anywhere is never edited; a
// correction is a later migration, because the store on the other machine has
// already applied this text.
public sealed record Migration(int Version, string Name, string Sql);
