namespace Bathos.Platform.Files;

/// <summary>Atomic publication of a complete staged file, including replacement while readers retain the previous snapshot.</summary>
/// <remarks>
/// Uses the rename contract for exclusively owned regular staging files on the same filesystem. With replacement
/// authorized, an absent destination is created and an occupied file is atomically replaced. Existing readers that
/// permit deletion/replacement can finish reading the previous file; subsequent opens see the new file.
/// This does not merge destination metadata, preserve its file identity, coordinate writers, or guarantee power-loss durability.
/// Hosts retain write coordination and destination policy. Links, special files, and directory relocation are outside
/// this capability's contract. Unsupported filesystem semantics fail explicitly without a weaker fallback.
/// </remarks>
public interface ISnapshotFileRename : INativeFileRename;
