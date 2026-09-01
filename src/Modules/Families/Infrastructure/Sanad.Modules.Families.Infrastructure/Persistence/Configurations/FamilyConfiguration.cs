using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class FamilyConfiguration :
    IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable("families");

        builder.HasKey(family => family.Id);

        builder.Property(family => family.Id)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(family => family.OwnerUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("owner_user_id")
            .IsRequired();

        builder.Property(family => family.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(family => family.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(family => family.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(family => family.OwnerUserId)
            .IsUnique();

        builder.OwnsMany(
            family => family.Members,
            member =>
            {
                member.ToTable("family_members");
                member.WithOwner().HasForeignKey("FamilyId");
                member.Property<FamilyId>("FamilyId")
                    .HasConversion(id => id.Value, value => new FamilyId(value));

                member.Property(m => m.Id)
                    .HasConversion(id => id.Value, value => new UserId(value))
                    .HasColumnName("user_id");
                member.HasKey("FamilyId", "Id");

                member.Property(m => m.AddedByUserId)
                    .HasConversion(id => id.Value, value => new UserId(value))
                    .HasColumnName("added_by_user_id")
                    .IsRequired();

                member.Property(m => m.RelationshipType)
                    .HasColumnName("relationship_type")
                    .IsRequired();

                member.Property(m => m.Role)
                    .HasColumnName("role")
                    .IsRequired();

                member.Property(m => m.JoinedOnUtc)
                    .HasColumnName("joined_on_utc")
                    .IsRequired();
            });

        builder.Ignore(family => family.DomainEvents);
    }
}