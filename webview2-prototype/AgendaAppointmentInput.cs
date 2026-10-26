namespace CentralWhatsApp.WebView2;

internal sealed record AgendaAppointmentInput(
    string Kind, string Client, string Responsible, string Phone,
    string Email, string Address, string[] ServiceIds, string PrimaryService,
    string Technician, string Notes, string Date, string Time, int Duration, string Status = "agendado", string? CompletedDate = null);

