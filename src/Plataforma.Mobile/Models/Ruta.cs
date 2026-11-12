namespace Plataforma.Mobile.Models;

using Plataforma.Domain;

public sealed record Ruta(
	Guid Id,
	DateOnly Fecha,
	string Operador,
	IReadOnlyList<Envio> Envios,
	IReadOnlyList<string>? Funcionalidades = null,
	bool AdmiteEntregaParcial = false,
	bool ExigeFirma = false,
	bool ExigeFoto = false,
	IReadOnlyList<string>? MotivosNoEntrega = null,
	PoliticaBultosPendientes PoliticaBultosPendientes = PoliticaBultosPendientes.NuevaVisitaSinRecargo,
	bool ExigeDocumentoReceptor = false,
	IReadOnlyList<RetiroEnRuta>? Retiros = null)
{
	public bool EntregaParcialHabilitada => AdmiteEntregaParcial &&
		(Funcionalidades?.Contains("entrega-parcial", StringComparer.Ordinal) ?? false);
}
