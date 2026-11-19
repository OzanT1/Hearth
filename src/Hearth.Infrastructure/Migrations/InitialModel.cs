// Frozen initial relational model. Do not replace this with runtime model configuration.
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
namespace Hearth.Infrastructure.Migrations;
internal static class InitialModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.10").HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        modelBuilder.Entity("Hearth.Infrastructure.AppUser", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("UserName").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<string>("NormalizedUserName").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<string>("Email").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<string>("NormalizedEmail").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<bool>("EmailConfirmed").HasColumnType("boolean");
            b.Property<string>("PasswordHash").HasColumnType("text");
            b.Property<string>("SecurityStamp").HasColumnType("text");
            b.Property<string>("ConcurrencyStamp").IsConcurrencyToken().HasColumnType("text");
            b.Property<string>("PhoneNumber").HasColumnType("text");
            b.Property<bool>("PhoneNumberConfirmed").HasColumnType("boolean");
            b.Property<bool>("TwoFactorEnabled").HasColumnType("boolean");
            b.Property<DateTimeOffset?>("LockoutEnd").HasColumnType("timestamp with time zone");
            b.Property<bool>("LockoutEnabled").HasColumnType("boolean");
            b.Property<int>("AccessFailedCount").HasColumnType("integer");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<bool>("AllowSharedServerDms").HasColumnType("boolean");
            b.HasKey("Id");
            b.HasIndex("NormalizedUserName").IsUnique().HasDatabaseName("UserNameIndex");
            b.HasIndex("NormalizedEmail").IsUnique().HasDatabaseName("EmailIndex");
            b.ToTable("AspNetUsers");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("Name").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<string>("NormalizedName").HasMaxLength(256).HasColumnType("character varying(256)");
            b.Property<string>("ConcurrencyStamp").IsConcurrencyToken().HasColumnType("text");
            b.HasKey("Id");
            b.HasIndex("NormalizedName").IsUnique().HasDatabaseName("RoleNameIndex");
            b.ToTable("AspNetRoles");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn).HasColumnType("integer");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("ClaimType").HasColumnType("text");
            b.Property<string>("ClaimValue").HasColumnType("text");
            b.HasKey("Id");
            b.HasIndex("UserId");
            b.ToTable("AspNetUserClaims");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn).HasColumnType("integer");
            b.Property<Guid>("RoleId").HasColumnType("uuid");
            b.Property<string>("ClaimType").HasColumnType("text");
            b.Property<string>("ClaimValue").HasColumnType("text");
            b.HasKey("Id");
            b.HasIndex("RoleId");
            b.ToTable("AspNetRoleClaims");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", b =>
        {
            b.Property<string>("LoginProvider").IsRequired().HasColumnType("text");
            b.Property<string>("ProviderKey").IsRequired().HasColumnType("text");
            b.Property<string>("ProviderDisplayName").HasColumnType("text");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("LoginProvider", "ProviderKey");
            b.HasIndex("UserId");
            b.ToTable("AspNetUserLogins");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("RoleId").HasColumnType("uuid");
            b.HasKey("UserId", "RoleId");
            b.HasIndex("RoleId");
            b.ToTable("AspNetUserRoles");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("LoginProvider").IsRequired().HasColumnType("text");
            b.Property<string>("Name").IsRequired().HasColumnType("text");
            b.Property<string>("Value").HasColumnType("text");
            b.HasKey("UserId", "LoginProvider", "Name");
            b.ToTable("AspNetUserTokens");
        });
        modelBuilder.Entity("Hearth.Domain.Server", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<Guid>("OwnerId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("OwnerId");
            b.ToTable("Servers");
        });
        modelBuilder.Entity("Hearth.Domain.Membership", b =>
        {
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("JoinedAt").HasColumnType("timestamp with time zone");
            b.HasKey("ServerId", "UserId");
            b.HasIndex("UserId");
            b.ToTable("Memberships");
        });
        modelBuilder.Entity("Hearth.Domain.ServerRole", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<int>("Permissions").HasColumnType("integer");
            b.Property<bool>("IsDefault").HasColumnType("boolean");
            b.HasKey("Id");
            b.HasAlternateKey("ServerId", "Id");
            b.HasIndex("ServerId").IsUnique().HasFilter("\"IsDefault\" = true");
            b.ToTable("ServerRoles");
        });
        modelBuilder.Entity("Hearth.Domain.RoleAssignment", b =>
        {
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<Guid>("RoleId").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("ServerId", "RoleId", "UserId");
            b.HasIndex("ServerId", "UserId");
            b.ToTable("RoleAssignments");
        });
        modelBuilder.Entity("Hearth.Domain.Conversation", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid?>("ServerId").HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<int>("Position").HasColumnType("integer");
            b.Property<bool>("IsPrivate").HasColumnType("boolean");
            b.Property<Guid?>("UserA").HasColumnType("uuid");
            b.Property<Guid?>("UserB").HasColumnType("uuid");
            b.HasKey("Id");
            b.HasIndex("UserA", "UserB").IsUnique().HasFilter("\"ServerId\" IS NULL");
            b.HasIndex("ServerId");
            b.ToTable("Conversations", t => t.HasCheckConstraint("CK_Conversation_Kind", "(\"ServerId\" IS NOT NULL AND \"UserA\" IS NULL AND \"UserB\" IS NULL) OR (\"ServerId\" IS NULL AND \"UserA\" IS NOT NULL AND \"UserB\" IS NOT NULL AND \"UserA\" <> \"UserB\")"));
        });
        modelBuilder.Entity("Hearth.Domain.ChannelGrant", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("ConversationId").HasColumnType("uuid");
            b.Property<Guid?>("RoleId").HasColumnType("uuid");
            b.Property<Guid?>("UserId").HasColumnType("uuid");
            b.HasKey("Id");
            b.HasIndex("ConversationId");
            b.HasIndex("RoleId");
            b.ToTable("ChannelGrants", t => t.HasCheckConstraint("CK_Grant_Target", "(\"RoleId\" IS NULL) <> (\"UserId\" IS NULL)"));
        });
        modelBuilder.Entity("Hearth.Domain.Message", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn).HasColumnType("bigint");
            b.Property<Guid>("ConversationId").HasColumnType("uuid");
            b.Property<Guid>("AuthorId").HasColumnType("uuid");
            b.Property<Guid>("ClientId").HasColumnType("uuid");
            b.Property<string>("Content").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.Property<long?>("ReplyToId").HasColumnType("bigint");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("EditedAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("Deleted").HasColumnType("boolean");
            b.HasKey("Id");
            b.HasIndex("ConversationId", "Id");
            b.HasIndex("AuthorId", "ClientId").IsUnique();
            b.ToTable("Messages");
        });
        modelBuilder.Entity("Hearth.Domain.Reaction", b =>
        {
            b.Property<long>("MessageId").HasColumnType("bigint");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Emoji").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.HasKey("MessageId", "UserId", "Emoji");
            b.ToTable("Reactions");
        });
        modelBuilder.Entity("Hearth.Domain.ReadPosition", b =>
        {
            b.Property<Guid>("ConversationId").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<long>("MessageId").HasColumnType("bigint");
            b.HasKey("ConversationId", "UserId");
            b.ToTable("ReadPositions");
        });
        modelBuilder.Entity("Hearth.Domain.Friendship", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("UserA").HasColumnType("uuid");
            b.Property<Guid>("UserB").HasColumnType("uuid");
            b.Property<Guid>("RequestedBy").HasColumnType("uuid");
            b.Property<int>("State").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("UserA", "UserB").IsUnique();
            b.ToTable("Friendships");
        });
        modelBuilder.Entity("Hearth.Domain.Block", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("TargetId").HasColumnType("uuid");
            b.HasKey("UserId", "TargetId");
            b.ToTable("Blocks");
        });
        modelBuilder.Entity("Hearth.Domain.Ban", b =>
        {
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("ServerId", "UserId");
            b.ToTable("Bans");
        });
        modelBuilder.Entity("Hearth.Domain.Invitation", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<int?>("MaxUses").HasColumnType("integer");
            b.Property<int>("Uses").HasColumnType("integer");
            b.Property<bool>("Revoked").HasColumnType("boolean");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("ServerId");
            b.ToTable("Invitations");
        });
        modelBuilder.Entity("Hearth.Domain.Attachment", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("ConversationId").HasColumnType("uuid");
            b.Property<Guid>("UploaderId").HasColumnType("uuid");
            b.Property<long?>("MessageId").HasColumnType("bigint");
            b.Property<string>("FileName").IsRequired().HasColumnType("text");
            b.Property<string>("StorageKey").IsRequired().HasColumnType("text");
            b.Property<string>("ContentType").IsRequired().HasColumnType("text");
            b.Property<long>("Size").HasColumnType("bigint");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("CreatedAt");
            b.HasIndex("ConversationId");
            b.HasIndex("MessageId");
            b.ToTable("Attachments");
        });
        modelBuilder.Entity("Hearth.Domain.OutboxEvent", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn).HasColumnType("bigint");
            b.Property<Guid?>("ConversationId").HasColumnType("uuid");
            b.Property<Guid?>("ServerId").HasColumnType("uuid");
            b.Property<Guid?>("UserId").HasColumnType("uuid");
            b.Property<string>("Kind").IsRequired().HasColumnType("text");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<int>("Attempts").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("CreatedAt");
            b.ToTable("Outbox");
        });
        modelBuilder.Entity("Hearth.Domain.AuditEvent", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn).HasColumnType("bigint");
            b.Property<Guid>("ServerId").HasColumnType("uuid");
            b.Property<Guid>("ActorId").HasColumnType("uuid");
            b.Property<Guid?>("TargetId").HasColumnType("uuid");
            b.Property<string>("Action").IsRequired().HasColumnType("text");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("ServerId", "Id");
            b.ToTable("Audit");
        });
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", b => b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>", null).WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", b => b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>", null).WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Server", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("OwnerId").OnDelete(DeleteBehavior.Restrict).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Membership", b => b.HasOne("Hearth.Domain.Server", null).WithMany().HasForeignKey("ServerId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Membership", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.ServerRole", b => b.HasOne("Hearth.Domain.Server", null).WithMany().HasForeignKey("ServerId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.RoleAssignment", b => b.HasOne("Hearth.Domain.ServerRole", null).WithMany().HasForeignKey("ServerId", "RoleId").HasPrincipalKey("ServerId", "Id").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.RoleAssignment", b => b.HasOne("Hearth.Domain.Membership", null).WithMany().HasForeignKey("ServerId", "UserId").HasPrincipalKey("ServerId", "UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Conversation", b => b.HasOne("Hearth.Domain.Server", null).WithMany().HasForeignKey("ServerId").OnDelete(DeleteBehavior.Cascade));
        modelBuilder.Entity("Hearth.Domain.ChannelGrant", b => b.HasOne("Hearth.Domain.Conversation", null).WithMany().HasForeignKey("ConversationId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.ChannelGrant", b => b.HasOne("Hearth.Domain.ServerRole", null).WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade));
        modelBuilder.Entity("Hearth.Domain.Message", b => b.HasOne("Hearth.Domain.Conversation", null).WithMany().HasForeignKey("ConversationId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Message", b => b.HasOne("Hearth.Infrastructure.AppUser", null).WithMany().HasForeignKey("AuthorId").OnDelete(DeleteBehavior.Restrict).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Reaction", b => b.HasOne("Hearth.Domain.Message", null).WithMany().HasForeignKey("MessageId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.ReadPosition", b => b.HasOne("Hearth.Domain.Conversation", null).WithMany().HasForeignKey("ConversationId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Ban", b => b.HasOne("Hearth.Domain.Server", null).WithMany().HasForeignKey("ServerId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Invitation", b => b.HasOne("Hearth.Domain.Server", null).WithMany().HasForeignKey("ServerId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Attachment", b => b.HasOne("Hearth.Domain.Conversation", null).WithMany().HasForeignKey("ConversationId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Hearth.Domain.Attachment", b => b.HasOne("Hearth.Domain.Message", null).WithMany().HasForeignKey("MessageId").OnDelete(DeleteBehavior.SetNull));
    }
}
