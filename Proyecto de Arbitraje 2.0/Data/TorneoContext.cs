using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ProyectoArbitraje.Models;

namespace ProyectoArbitraje.Data;

public partial class TorneoContext : DbContext
{
    public TorneoContext()
    {
    }

    public TorneoContext(DbContextOptions<TorneoContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Atleta> Atletas { get; set; }

    public virtual DbSet<ByeFaseFinal> ByesFaseFinal { get; set; }

    public virtual DbSet<Cancha> Canchas { get; set; }

    public virtual DbSet<Categoria> Categorias { get; set; }

    public virtual DbSet<CompetidorIntegrante> CompetidorIntegrantes { get; set; }

    public virtual DbSet<Competidore> Competidores { get; set; }

    public virtual DbSet<Configuracion> Configuracions { get; set; }

    public virtual DbSet<Grupo> Grupos { get; set; }

    public virtual DbSet<Municipio> Municipios { get; set; }

    public virtual DbSet<Partido> Partidos { get; set; }

    public virtual DbSet<PuntoPartido> PuntosPartido { get; set; }

    public virtual DbSet<RankingActual> RankingActuals { get; set; }

    public virtual DbSet<RankingCombinadoPorAtletum> RankingCombinadoPorAtleta { get; set; }

    public virtual DbSet<RankingHistorial> RankingHistorials { get; set; }

    public virtual DbSet<SetsPartido> SetsPartidos { get; set; }

    public virtual DbSet<TablaPuntosRanking> TablaPuntosRankings { get; set; }

    public virtual DbSet<Torneo> Torneos { get; set; }

    public virtual DbSet<Usuario> Usuarios { get; set; } 

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=TorneoBadminton;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Atleta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Atletas__3214EC07D954D801");

            entity.Property(e => e.Genero).HasMaxLength(1);
            entity.Property(e => e.Nombre).HasMaxLength(100);

            entity.HasOne(d => d.Municipio).WithMany(p => p.Atleta)
                .HasForeignKey(d => d.MunicipioId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Atletas__Municip__5165187F");
        });

        modelBuilder.Entity<Cancha>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Canchas__3214EC0768EC1722");

            entity.HasIndex(e => new { e.TorneoId, e.Numero }, "UQ_Cancha").IsUnique();

            entity.HasOne(d => d.Torneo).WithMany(p => p.Canchas)
                .HasForeignKey(d => d.TorneoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Canchas__TorneoI__628FA481");
        });

        modelBuilder.Entity<ByeFaseFinal>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(d => d.Categoria).WithMany()
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.Competidor).WithMany()
                .HasForeignKey(d => d.CompetidorId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Categori__3214EC07F28FAAD7");

            entity.HasIndex(e => new { e.TorneoId, e.Nombre, e.Modalidad, e.Rama }, "UQ_Categoria").IsUnique();

            entity.Property(e => e.Formato)
                .HasMaxLength(20)
                .HasDefaultValue("GruposFaseFinal");
            entity.Property(e => e.Modalidad).HasMaxLength(10);
            entity.Property(e => e.Nombre).HasMaxLength(50);
            entity.Property(e => e.Rama).HasMaxLength(10);

            entity.HasOne(d => d.Torneo).WithMany(p => p.Categoria)
                .HasForeignKey(d => d.TorneoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Categoria__Torne__5629CD9C");
        });

        modelBuilder.Entity<CompetidorIntegrante>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Competid__3214EC07AFA527C5");

            entity.ToTable(tb =>
            {
                tb.HasTrigger("TRG_CompetidorIntegrantes_MismoMunicipio");
                tb.HasTrigger("TRG_CompetidorIntegrantes_NoDuplicadoEnCategoria");
                tb.HasTrigger("TRG_CompetidorIntegrantes_CantidadSegunModalidad");
                tb.HasTrigger("TRG_CompetidorIntegrantes_GeneroSegunRama");
            });

            entity.HasIndex(e => new { e.CompetidorId, e.AtletaId }, "UQ_CompetidorAtleta").IsUnique();

            entity.HasOne(d => d.Atleta).WithMany(p => p.CompetidorIntegrantes)
                .HasForeignKey(d => d.AtletaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Competido__Atlet__71D1E811");

            entity.HasOne(d => d.Competidor).WithMany(p => p.CompetidorIntegrantes)
                .HasForeignKey(d => d.CompetidorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Competido__Compe__70DDC3D8");
        });

        modelBuilder.Entity<Competidore>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Competid__3214EC07F2B2C9B0");

            entity.ToTable(tb => tb.HasTrigger("TRG_Competidores_GrupoSegunFormato"));

            entity.Property(e => e.Pc).HasColumnName("PC");
            entity.Property(e => e.Pf).HasColumnName("PF");
            entity.Property(e => e.Pg).HasColumnName("PG");
            entity.Property(e => e.Pp).HasColumnName("PP");
            entity.Property(e => e.Sg).HasColumnName("SG");
            entity.Property(e => e.Sp).HasColumnName("SP");

            entity.HasOne(d => d.Categoria).WithMany(p => p.Competidores)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Competido__Categ__656C112C");

            entity.HasOne(d => d.Grupo).WithMany(p => p.Competidores)
                .HasForeignKey(d => d.GrupoId)
                .HasConstraintName("FK__Competido__Grupo__66603565");
        });

        modelBuilder.Entity<Configuracion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Configur__3214EC077CB7A96E");

            entity.ToTable("Configuracion");

            entity.HasIndex(e => e.CategoriaId, "UQ_Configuracion_Categoria").IsUnique();

            entity.Property(e => e.ModalidadSets)
                .HasMaxLength(20)
                .HasDefaultValue("2 de 3 Sets");
            entity.Property(e => e.ModalidadSetsFinal).HasMaxLength(20);

            entity.HasOne(d => d.Categoria).WithOne(p => p.Configuracion)
                .HasForeignKey<Configuracion>(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Configuracion_Categoria");
        });

        modelBuilder.Entity<Grupo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Grupos__3214EC079C93539C");

            entity.HasIndex(e => new { e.CategoriaId, e.Letra }, "UQ_Grupo").IsUnique();

            entity.Property(e => e.Letra).HasMaxLength(5);

            entity.HasOne(d => d.Categoria).WithMany(p => p.Grupos)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Grupos__Categori__5EBF139D");
        });

        modelBuilder.Entity<Municipio>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Municipi__3214EC07E904D778");

            entity.HasIndex(e => e.Nombre, "UQ__Municipi__75E3EFCFAEC598E8").IsUnique();

            entity.Property(e => e.Abreviacion).HasMaxLength(10);
            entity.Property(e => e.Nombre).HasMaxLength(50);
        });

        modelBuilder.Entity<Partido>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Partidos__3214EC0701D05BF3");

            entity.ToTable(tb => tb.HasTrigger("TRG_Partidos_GanadorValido"));

            entity.HasIndex(e => new { e.CategoriaId, e.Fase }, "IX_Partidos_Categoria");

            entity.HasIndex(e => e.Estado, "IX_Partidos_Estado");

            entity.HasIndex(e => new { e.CategoriaId, e.Jornada }, "IX_Partidos_Jornada");

            entity.Property(e => e.CompetidorAid).HasColumnName("CompetidorAId");
            entity.Property(e => e.CompetidorBid).HasColumnName("CompetidorBId");
            entity.Property(e => e.Estado)
                .HasMaxLength(15)
                .HasDefaultValue("Pendiente");
            entity.Property(e => e.Fase)
                .HasMaxLength(20)
                .HasDefaultValue("Grupos");
            entity.Property(e => e.CapturadoPor).HasMaxLength(50);
            entity.Property(e => e.CorregidoPor).HasMaxLength(50);
            entity.Property(e => e.MarcadorAnterior).HasMaxLength(100);

            entity.HasOne(d => d.Cancha).WithMany(p => p.Partidos)
                .HasForeignKey(d => d.CanchaId)
                .HasConstraintName("FK__Partidos__Cancha__7C4F7684");

            entity.HasOne(d => d.Categoria).WithMany(p => p.Partidos)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Partidos__Catego__75A278F5");

            entity.HasOne(d => d.CompetidorA).WithMany(p => p.PartidoCompetidorAs)
                .HasForeignKey(d => d.CompetidorAid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Partidos__Compet__778AC167");

            entity.HasOne(d => d.CompetidorB).WithMany(p => p.PartidoCompetidorBs)
                .HasForeignKey(d => d.CompetidorBid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Partidos__Compet__787EE5A0");

            entity.HasOne(d => d.Ganador).WithMany(p => p.PartidoGanadors)
                .HasForeignKey(d => d.GanadorId)
                .HasConstraintName("FK__Partidos__Ganado__797309D9");

            entity.HasOne(d => d.Grupo).WithMany(p => p.Partidos)
                .HasForeignKey(d => d.GrupoId)
                .HasConstraintName("FK__Partidos__GrupoI__76969D2E");
        });

        modelBuilder.Entity<PuntoPartido>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.PartidoId, e.NumeroSet, e.NumeroPunto }, "UQ_PuntoPartido").IsUnique();

            entity.Property(e => e.EquipoAnoto).HasMaxLength(1);

            entity.HasOne(d => d.Partido).WithMany(p => p.PuntosPartido)
                .HasForeignKey(d => d.PartidoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.SirveAtleta).WithMany()
                .HasForeignKey(d => d.SirveAtletaId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.RecibeAtleta).WithMany()
                .HasForeignKey(d => d.RecibeAtletaId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<RankingActual>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("RankingActual");

            entity.Property(e => e.Modalidad).HasMaxLength(10);
            entity.Property(e => e.Rama).HasMaxLength(10);
        });

        modelBuilder.Entity<RankingCombinadoPorAtletum>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("RankingCombinadoPorAtleta");

            entity.Property(e => e.Rama).HasMaxLength(10);
        });

        modelBuilder.Entity<RankingHistorial>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__RankingH__3214EC079CE51CD6");

            entity.ToTable("RankingHistorial", tb => tb.HasTrigger("TRG_RankingHistorial_NoJornadas"));

            entity.HasIndex(e => new { e.AtletaId, e.TorneoId, e.CategoriaId }, "UQ_RankingHistorial").IsUnique();

            entity.HasOne(d => d.Atleta).WithMany(p => p.RankingHistorials)
                .HasForeignKey(d => d.AtletaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RankingHi__Atlet__0F624AF8");

            entity.HasOne(d => d.Categoria).WithMany(p => p.RankingHistorials)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RankingHi__Categ__114A936A");

            entity.HasOne(d => d.Torneo).WithMany(p => p.RankingHistorials)
                .HasForeignKey(d => d.TorneoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RankingHi__Torne__10566F31");
        });

        modelBuilder.Entity<SetsPartido>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__SetsPart__3214EC07497977ED");

            entity.ToTable("SetsPartido");

            entity.HasIndex(e => new { e.PartidoId, e.NumeroSet }, "UQ_Set_Partido").IsUnique();

            entity.HasOne(d => d.Partido).WithMany(p => p.SetsPartidos)
                .HasForeignKey(d => d.PartidoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SetsParti__Parti__02084FDA");
        });

        modelBuilder.Entity<TablaPuntosRanking>(entity =>
        {
            entity.HasKey(e => e.Posicion).HasName("PK__TablaPun__E319A809D5AB78AE");

            entity.ToTable("TablaPuntosRanking");

            entity.Property(e => e.Posicion).ValueGeneratedNever();
        });

        modelBuilder.Entity<Torneo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Torneos__3214EC07C6969B4B");

            entity.Property(e => e.Estado)
                .HasMaxLength(15)
                .HasDefaultValue("Planeado");
            entity.Property(e => e.Nombre).HasMaxLength(100);
            entity.Property(e => e.Modalidad)
                .HasMaxLength(20)
                .HasDefaultValue("GruposFaseFinal");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NombreUsuario).IsUnique();
            entity.Property(e => e.NombreUsuario).HasMaxLength(50);
            entity.Property(e => e.Rol).HasMaxLength(20);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}