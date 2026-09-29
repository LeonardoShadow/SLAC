using SLAC.Core.Docentes.Entities;
using SLAC.Core.Estudiantes.Entities;

namespace SLAC.Features.Attendance.Services;

public enum RolUsuario
{
    Ninguno,
    Administrador,
    Docente,
    Estudiante
}

public class UsuarioSesion
{
    public Guid Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; } = RolUsuario.Ninguno;
    public Guid InstitucionId { get; set; }
    public Guid? DocenteId { get; set; }
    public Guid? EstudianteId { get; set; }
}

public interface IUserSessionService
{
    UsuarioSesion? UsuarioActual { get; }
    bool EstaAutenticado { get; }
    RolUsuario RolActual { get; }
    event Action? OnChange;

    void IniciarSesionAdmin(Guid institucionId, string nombre = "Administrador Institucional", string correo = "admin@upds.net.bo");
    void IniciarSesionDocente(Docente docente);
    void IniciarSesionEstudiante(Estudiante estudiante);
    void CerrarSesion();
}

public class UserSessionService : IUserSessionService
{
    public UsuarioSesion? UsuarioActual { get; private set; }

    public bool EstaAutenticado => UsuarioActual != null && UsuarioActual.Rol != RolUsuario.Ninguno;

    public RolUsuario RolActual => UsuarioActual?.Rol ?? RolUsuario.Ninguno;

    public event Action? OnChange;

    public void IniciarSesionAdmin(Guid institucionId, string nombre = "Administrador Institucional", string correo = "admin@upds.net.bo")
    {
        UsuarioActual = new UsuarioSesion
        {
            Id = Guid.NewGuid(),
            NombreCompleto = nombre,
            Correo = correo,
            Codigo = "ADMIN-01",
            Rol = RolUsuario.Administrador,
            InstitucionId = institucionId
        };
        NotifyStateChanged();
    }

    public void IniciarSesionDocente(Docente docente)
    {
        UsuarioActual = new UsuarioSesion
        {
            Id = docente.Id,
            NombreCompleto = docente.NombreCompleto,
            Correo = docente.Correo,
            Codigo = docente.Nombres,
            Rol = RolUsuario.Docente,
            InstitucionId = docente.InstitucionId,
            DocenteId = docente.Id
        };
        NotifyStateChanged();
    }

    public void IniciarSesionEstudiante(Estudiante estudiante)
    {
        UsuarioActual = new UsuarioSesion
        {
            Id = estudiante.Id,
            NombreCompleto = estudiante.NombreCompleto,
            Correo = estudiante.Correo,
            Codigo = estudiante.Codigo,
            Rol = RolUsuario.Estudiante,
            InstitucionId = estudiante.InstitucionId,
            EstudianteId = estudiante.Id
        };
        NotifyStateChanged();
    }

    public void CerrarSesion()
    {
        UsuarioActual = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
