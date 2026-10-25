using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Idempotency;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRequestConfiguration
    : IEntityTypeConfiguration<IdempotencyRequest>
{
    public void Configure(EntityTypeBuilder<IdempotencyRequest> builder)
    {
        builder.ToTable("idempotency_requests");

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(request => request.ActorId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_id")
            .IsRequired();

        builder.Property(request => request.Operation)
            .HasMaxLength(IdempotencyRequest.MaximumOperationLength)
            .HasColumnName("operation")
            .IsRequired();

        builder.Property(request => request.Key)
            .HasMaxLength(IdempotencyRequest.MaximumKeyLength)
            .HasColumnName("idempotency_key")
            .IsRequired();

        builder.Property(request => request.Fingerprint)
            .HasMaxLength(IdempotencyRequest.FingerprintLength)
            .HasColumnType("char(64)")
            .HasColumnName("fingerprint")
            .IsRequired();

        builder.Property(request => request.ResourceId)
            .HasColumnName("resource_id");

        builder.Property(request => request.ResponseStatus)
            .HasColumnName("response_status")
            .IsRequired();

        builder.Property(request => request.ResponseBody)
            .HasColumnType("text")
            .HasColumnName("response_body")
            .IsRequired();

        builder.Property(request => request.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        // Exactly-once boundary for a retried create.
        builder.HasIndex(request => new
            {
                request.ActorId,
                request.Operation,
                request.Key
            })
            .IsUnique()
            .HasDatabaseName(IdempotencyRequest.UniqueIndexName);

        // Retention is 7 days by policy; indexed for a future purge.
        builder.HasIndex(request => request.CreatedOnUtc)
            .HasDatabaseName(IdempotencyRequest.CreatedOnIndexName);
    }
}
