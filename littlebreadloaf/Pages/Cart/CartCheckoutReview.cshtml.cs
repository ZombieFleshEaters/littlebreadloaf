using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using littlebreadloaf.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.IO;
using SelectPdf;

namespace littlebreadloaf.Pages.Cart
{
    public class CartCheckoutReviewModel : PageModel
    {
        private readonly ProductContext _context;
        private readonly IConfiguration _config;

        private const int Number_of_days_till_due = 14;

        public CartCheckoutReviewModel(ProductContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [BindProperty]
        public ProductOrder ProductOrder { get; set; }
       
        [BindProperty(SupportsGet = true)]
        public string ProductOrderID { get; set; }

        [BindProperty]
        public NzAddressDeliverable NzAddressDeliverable { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrEmpty(ProductOrderID) || !Guid.TryParse(ProductOrderID, out Guid productOrderID))
            {
                return new RedirectToPageResult("/Cart/CartView");
            }

            ProductOrder = await _context.ProductOrder.FirstOrDefaultAsync(f => f.OrderID == productOrderID);
            if(ProductOrder == null)
            {
                return new RedirectToPageResult("/Cart/CartView");
            }

            NzAddressDeliverable = await _context.NzAddressDeliverable.FirstOrDefaultAsync(f => f.address_id == ProductOrder.ContactAddress);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            ProductOrder.Confirmed = DateTime.Now;
            while (true)
            {
                ProductOrder.ConfirmationCode = GenerateConfirmationNumber();
                if (!await _context.ProductOrder.AnyAsync(a => a.ConfirmationCode == ProductOrder.ConfirmationCode && a.ContactEmail.Equals(ProductOrder.ContactEmail, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }
            }

            //Create invoicing records
            var invoice = new Invoice()
            {
                InvoiceID = Guid.NewGuid(),
                ProductOrderID = ProductOrder.OrderID.Value,
                Created = DateTime.Now,
                Due = InvoiceHelper.GetDueDate(Number_of_days_till_due)
            };

            List<InvoiceTransaction> invoiceTransactions = await  _context
                                                                    .Product
                                                                    .Join(_context.CartItem,
                                                                            p => p.ProductID,
                                                                            ci => ci.ProductID,
                                                                            (p, ci) => new { p.ProductID, p.Price, p.Name, p.Description, ci.CartItemID, ci.CartID, ci.Quantity })
                                                                    .Where(w => w.CartID == ProductOrder.CartID)
                                                                    .Select(s => new InvoiceTransaction()
                                                                    {
                                                                        InvoiceID = invoice.InvoiceID,
                                                                        Type = InvoiceHelper.Transaction_Type_Debit,
                                                                        Category = InvoiceHelper.Transaction_Catgory_Product,
                                                                        Name = s.Name,
                                                                        Description = s.Description,
                                                                        Quantity = s.Quantity.Value,
                                                                        Price = s.Price.Value,
                                                                        Posted = DateTime.Now
                                                                    }).ToListAsync();

            invoiceTransactions.ConvertAll(c => c.InvoiceTransactionID = Guid.NewGuid());

            _context.Invoice.Add(invoice);
            _context.InvoiceTransaction.AddRange(invoiceTransactions);
            _context.ProductOrder.Update(ProductOrder);
            await _context.SaveChangesAsync();

            var url = Url.Page("/Orders/InvoicePrint", new { ProductOrder.OrderID });
            var absUrl = string.Format("{0}://{1}{2}", Request.Scheme, Request.Host, url);

            HtmlToPdf converter = new SelectPdf.HtmlToPdf();

            converter.Options.MarginBottom = 20;
            converter.Options.MarginTop = 20;
            converter.Options.MarginRight = 20;
            converter.Options.MarginLeft = 20;
            PdfDocument doc = converter.ConvertUrl(absUrl);
            
            using (var msInvoice = new System.IO.MemoryStream())
            {
                doc.Save(msInvoice);
                // close pdf document
                doc.Close();

                msInvoice.Position = 0;
                //Send confirmation email
                var emailResponse = await EmailHelper.SendEmail(_config,
                                                                $"Little Bread Loaf <mailgun@{_config["Mailgun.Uri.Request"]}>",
                                                                ProductOrder.ContactEmail,
                                                                "Order Confirmed",
                                                                "Thank you for your order! Confirmation number: " + ProductOrder.ConfirmationCode,
                                                                "little-bread-loaf-confirmation-" + ProductOrder.ConfirmationCode,
                                                                msInvoice);
                string txtResponse;

                using (Stream receiveStream = await emailResponse.Content.ReadAsStreamAsync())
                {
                    using (StreamReader readStream = new StreamReader(receiveStream, System.Text.Encoding.UTF8))
                    {
                        txtResponse = readStream.ReadToEnd();
                    }
                }

                if (emailResponse.IsSuccessStatusCode)
                {
                    var test = "Hello";
                }


            }
            
            // save pdf document
          


           

            

            // Clear cart cookies
            HttpContext.Response.Cookies.Delete(littlebreadloaf.CartHelper.CartCookieName);

            
            return new RedirectToPageResult("/Cart/CartCheckoutConfirmation", new { ProductOrderID = ProductOrder.OrderID, ProductOrder.CartID });
        }

        private string GenerateConfirmationNumber()
        {
            var chars = "BCDFGHJKLMNPQRSTVWXYZ23456789"; //Remove vowels, zero, one, and I 
            var stringChars = new char[6];
            var random = new Random();

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return new String(stringChars);
        }

    }
}