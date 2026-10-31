using Ahlena.Modules.Families.Domain.Notes;

namespace Ahlena.API.Controllers.Requests;

public sealed record AddElderlyNoteRequest(
    string Title,
    string Description,
    NoteCategory Category,
    NotePriority Priority);

public sealed record UpdateElderlyNoteRequest(
    string Title,
    string Description,
    NoteCategory Category,
    NotePriority Priority);