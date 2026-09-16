using FluentValidation;
using NovaWallet.Application.DTOs;

namespace NovaWallet.Application.Validators;

public class CreateWalletRequestValidator : AbstractValidator<CreateWalletRequest>
{
    public CreateWalletRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer ID is required.")
            .MaximumLength(100).WithMessage("Customer ID must not exceed 100 characters.");

        RuleFor(x => x.Bvn)
            .Matches(@"^\d{11}$")
            .When(x => !string.IsNullOrEmpty(x.Bvn))
            .WithMessage("BVN must be an 11-digit number.");

        RuleFor(x => x.Nin)
            .Matches(@"^\d{11}$")
            .When(x => !string.IsNullOrEmpty(x.Nin))
            .WithMessage("NIN must be an 11-digit number.");
    }
}

public class CreditWalletRequestValidator : AbstractValidator<CreditWalletRequest>
{
    public CreditWalletRequestValidator()
    {
        RuleFor(x => x.AmountKobo)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero kobo.");

        RuleFor(x => x.Reference)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.Reference));
    }
}

public class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.SourceWalletId)
            .NotEmpty().WithMessage("Source wallet ID is required.");

        RuleFor(x => x.DestinationWalletId)
            .NotEmpty().WithMessage("Destination wallet ID is required.")
            .NotEqual(x => x.SourceWalletId).WithMessage("Source and destination wallets cannot be the same.");

        RuleFor(x => x.AmountKobo)
            .GreaterThan(0).WithMessage("Transfer amount must be greater than zero kobo.")
            .LessThanOrEqualTo(50_000_000).WithMessage("Single transfer amount cannot exceed ₦500,000 (50,000,000 kobo).");
    }
}
