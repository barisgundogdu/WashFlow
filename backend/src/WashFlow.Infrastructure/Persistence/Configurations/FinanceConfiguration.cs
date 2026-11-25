using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Common;
using WashFlow.Domain.Finance;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Domain.WorkOrders;
namespace WashFlow.Infrastructure.Persistence.Configurations;
public sealed class FinanceConfiguration : IEntityTypeConfiguration<Payment>,IEntityTypeConfiguration<ManualIncome>,IEntityTypeConfiguration<Expense>,IEntityTypeConfiguration<PaymentMethod>
{
    private static void Base<T>(EntityTypeBuilder<T> b,string table) where T:class,IEntity
    {
        b.ConfigureEntity(table);b.HasAlternateKey("BusinessId","Id");
        b.HasOne<Business>().WithMany().HasForeignKey("BusinessId").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey("ActorUserId").OnDelete(DeleteBehavior.Restrict);
    }
    private static void Entry<T>(EntityTypeBuilder<T> b,string table) where T:class,IEntity
    {
        Base(b,table);b.Property<decimal>("Amount").HasPrecision(9,2);b.Property<string>("Currency").HasMaxLength(3).IsRequired();
        b.Property<string>("RequestHash").HasMaxLength(64).IsRequired();b.Property<string>("Description").HasMaxLength(1000);b.Property<string>("Reference").HasMaxLength(200);b.Property<string>("CorrectionReason").HasMaxLength(500);
        b.HasIndex("BusinessId","RequestId").IsUnique();b.HasIndex("BusinessId","OccurredAtUtc","Id");
        b.HasIndex("BusinessId","ReversesId").IsUnique().HasFilter("\"ReversesId\" IS NOT NULL");
        b.HasOne<T>().WithMany().HasForeignKey("BusinessId","CorrectsId").HasPrincipalKey("BusinessId","Id").OnDelete(DeleteBehavior.Restrict);
        b.HasOne<T>().WithMany().HasForeignKey("BusinessId","ReversesId").HasPrincipalKey("BusinessId","Id").OnDelete(DeleteBehavior.Restrict);
        b.ToTable(table,t=>t.HasCheckConstraint("CK_"+table+"_Values","\"Amount\" BETWEEN 0.01 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND ((\"ReversesId\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"ReversesId\" IS NOT NULL AND \"ReversesId\" <> \"Id\" AND length(btrim(\"CorrectionReason\")) > 0))"));
    }
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        Entry(b,"Payments");b.HasOne<WorkOrder>().WithMany().HasForeignKey(x=>new{x.BusinessId,x.WorkOrderId}).HasPrincipalKey(x=>new{x.BusinessId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        Method(b);b.HasIndex(x=>new{x.BusinessId,x.WorkOrderId});
    }
    public void Configure(EntityTypeBuilder<ManualIncome> b){Entry(b,"ManualIncomes");b.NonBlank("ManualIncomes","Category",100);Method(b);}
    private static void Method<T>(EntityTypeBuilder<T> b)where T:class
    {
        b.Property<string>("PaymentMethodName").HasMaxLength(100).IsRequired();
        b.HasOne<PaymentMethod>().WithMany().HasForeignKey("BusinessId","PaymentMethodId").HasPrincipalKey("BusinessId","Id").OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        Entry(b,"Expenses");b.NonBlank("Expenses","Category",100);
        b.HasOne<BusinessMembership>().WithMany().HasForeignKey(x=>new{x.BusinessId,x.EmployeeMembershipId}).HasPrincipalKey(x=>new{x.BusinessId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        Base(b,"PaymentMethods");b.NonBlank("PaymentMethods","Name",100);b.HasIndex(x=>new{x.BusinessId,x.Name}).IsUnique();
    }
}
