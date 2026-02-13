namespace Basket.API.Features.StoreBasket;

using FluentValidation;

public sealed class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart).NotNull().WithMessage("Sepet boş olamaz.");
        RuleFor(x => x.Cart.UserName).NotEmpty().WithMessage("UserName zorunludur.");

        RuleFor(x => x.Cart.Items).NotEmpty().WithMessage("Sepette en az bir ürün olmalı.");

        RuleForEach(x => x.Cart.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("ProductId zorunludur.");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Miktar 0'dan büyük olmalı.");
            item.RuleFor(i => i.Price).GreaterThanOrEqualTo(0).WithMessage("Fiyat negatif olamaz.");
        });
    }
}