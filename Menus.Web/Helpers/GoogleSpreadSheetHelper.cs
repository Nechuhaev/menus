using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System;
using Google.Apis.Sheets.v4.Data;
using System.Linq;
using Menus.Web.Models.ViewModels;
using Menus.Abstract.Models;
using Menus.Web.Models;
using System.Runtime.CompilerServices;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using System.Net;

namespace Menus.Web.Helpers
{
    public class GoogleSpreadSheetHelper
    {
        public void UpdateSpreadSheetWithRestaurantData(string sheetId, IRestaurant restaturant, IRestaurantSeo seo, DishesListViewModel categoriesWithDishes)
        {
            SheetsService service = GetGoogleAuthService();
            IList<IList<Object>> restaurantData = GetRestaurantData(restaturant, categoriesWithDishes);

            UpdatGoogleSheetInBatch(restaurantData, sheetId, "A1:A", service);
        }

        public IList<IList<Object>> UpdateFromSpreadSheetWithRestaurantData(string sheetId)
        {
            SheetsService service = GetGoogleAuthService();
            String range = "A1:W500";
            SpreadsheetsResource.ValuesResource.GetRequest getRequest =
                       service.Spreadsheets.Values.Get(sheetId, range);

            ValueRange getResponse = getRequest.Execute();
            IList<IList<Object>> values = getResponse.Values;

            return values;
        }

        private SheetsService GetGoogleAuthService()
        {
            SheetsService service = new SheetsService();
            string[] Scopes = { SheetsService.Scope.Spreadsheets };
            string ApplicationName = "TipMyMeny spreadsheet";

            //UserCredential credential;

            //using (var stream = new FileStream("spreadsheet-api.json", FileMode.Open, FileAccess.Read))
            //{
            //    string credPath = "Credentials/sheets.googleapis.com-dotnet-quickstart.json";

            //    credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
            //        GoogleClientSecrets.Load(stream).Secrets,
            //        Scopes,
            //        "user",
            //        CancellationToken.None,
            //        new FileDataStore(credPath, true)).Result;
            //    Console.WriteLine("Credential file saved to: " + credPath);
            //}


            //------------
            GoogleCredential credential;
            using (var stream = new FileStream("spreadsheet-api.json", FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }


            //------------


            // Create Google Sheets API service.
            service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });


            return service;
        }

        protected string GetRange(SheetsService service)
        {
            // Define request parameters.
            String spreadsheetId = "1NdXmtC7vy0UHm1ODXU4FFPPuK-1B2FTKHNVvYB0nQqE";
            String range = "A:A";

            SpreadsheetsResource.ValuesResource.GetRequest getRequest =
                       service.Spreadsheets.Values.Get(spreadsheetId, range);

            ValueRange getResponse = getRequest.Execute();
            IList<IList<Object>> getValues = getResponse.Values;

            int currentCount = getValues.Count() + 2;

            String newRange = "A" + currentCount + ":A";

            return newRange;
        }

        private IList<IList<Object>> GenerateData()
        {
            List<IList<Object>> objNewRecords = new List<IList<Object>>();

            IList<Object> obj = new List<Object>();

            obj.Add("Column - 1");
            obj.Add("Column - 2");
            obj.Add("Column - 3");

            objNewRecords.Add(obj);

            return objNewRecords;
        }

        private IList<IList<Object>> GetRestaurantData(IRestaurant restaturant, DishesListViewModel categoriesWithDishes)
        {
            string ruLocal = "ru";
            string ukLocal = "uk";
            IList<Object> resObj = new List<Object>();
            List<IList<Object>> objNewRecords = new List<IList<Object>>();

            IList<Object> headerObj = new List<Object>();

            headerObj.Add("Назва ресторана УКР");

            headerObj.Add("Категорія блюда УКР");
            headerObj.Add("Категорія блюда РУС");


            headerObj.Add("ПОЗИЦИЯ УКР");
            headerObj.Add("ПОЗИЦИЯ РУС");

            headerObj.Add("Краткое описание позиции УКР");
            headerObj.Add("Краткое описание позиции РУС");

            headerObj.Add("Подробное описание позиции УКР");
            headerObj.Add("Подробное описание позиции РУС");

            headerObj.Add("Состав УКР");
            headerObj.Add("Состав РУС");


            headerObj.Add("ВЕС");
            headerObj.Add("ЕДИНИЦА");            
            headerObj.Add("ЦЕНА");

            headerObj.Add("Id");

            objNewRecords.Add(headerObj);

            resObj.Add(restaturant.Name);
            objNewRecords.Add(resObj);

            foreach (var category in categoriesWithDishes.Categories)
            {
                IList<Object> catObj = new List<Object>();
                catObj.Add(string.Empty);

                catObj.Add(((IMenuCategory)category).Name.Get(ukLocal));
                catObj.Add(((IMenuCategory)category).Name.Get(ruLocal));
                

                objNewRecords.Add(catObj);


                foreach (var dish in category.Dishes)
                {
                    IList<Object> obj = new List<Object>();

                    obj.Add(string.Empty);

                    obj.Add(string.Empty);
                    obj.Add(string.Empty);


                    obj.Add(dish.Name.Get(ukLocal));
                    obj.Add(dish.Name.Get(ruLocal));

                    obj.Add(dish.ShortDescription.Get(ukLocal));
                    obj.Add(dish.ShortDescription.Get(ruLocal));

                    obj.Add(dish.Description.Get(ukLocal));
                    obj.Add(dish.Description.Get(ruLocal));


                    obj.Add(dish.Ingredients.Get(ukLocal));
                    obj.Add(dish.Ingredients.Get(ruLocal));

                    if(dish.Weight.Split(" ", StringSplitOptions.RemoveEmptyEntries).Length > 1 && string.IsNullOrEmpty(dish.WeightType))
                    {
                        obj.Add(dish.Weight.Split(" ", StringSplitOptions.RemoveEmptyEntries)[0]);
                        obj.Add(dish.Weight.Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
                    } else
                    {
                        obj.Add(dish.Weight);
                        obj.Add(dish.WeightType != null ? dish.WeightType.ToString() : String.Empty);                        
                    }                    
                    obj.Add(dish.Price.ToString("0.00"));

                    obj.Add(dish.Id);

                    objNewRecords.Add(obj);
                }
            }

            return objNewRecords;
        }

        private void UpdatGoogleSheetInBatch(IList<IList<Object>> values, string spreadsheetId, string newRange, SheetsService service)
        {
            // The A1 notation of the values to clear.
            string clearRange = "A:Z";  // TODO: Update placeholder value.

            // TODO: Assign values to desired properties of `requestBody`:
            Google.Apis.Sheets.v4.Data.ClearValuesRequest clearRequestBody = new Google.Apis.Sheets.v4.Data.ClearValuesRequest();
            SpreadsheetsResource.ValuesResource.ClearRequest clearRequest = service.Spreadsheets.Values.Clear(clearRequestBody, spreadsheetId, clearRange);
            Google.Apis.Sheets.v4.Data.ClearValuesResponse clearResponse = clearRequest.Execute();

            SpreadsheetsResource.ValuesResource.AppendRequest request =
               service.Spreadsheets.Values.Append(new ValueRange() { Values = values }, spreadsheetId, newRange);
            request.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            var response = request.Execute();


            var userEnteredFormatClear = new CellFormat()
            {
                TextFormat = new TextFormat()
                {
                    Bold = false,
                    FontSize = 10
                }
            };

            var userEnteredFormatHeader = new CellFormat()
            {
                TextFormat = new TextFormat()
                {
                    Bold = true,
                    FontSize = 12
                }
            };

            var userEnteredFormat = new CellFormat()
            {
                TextFormat = new TextFormat()
                {
                    Bold = true,
                    FontSize = 10
                }
            };

            var clearCellsRequest = new Request()
            {
                RepeatCell = new RepeatCellRequest()
                {
                    Range = new Google.Apis.Sheets.v4.Data.GridRange()
                    {
                        SheetId = 0,
                        StartColumnIndex = 0,
                        StartRowIndex = 0,
                        EndColumnIndex = 50,
                        EndRowIndex = 500
                    },
                    Cell = new CellData()
                    {
                        UserEnteredFormat = userEnteredFormatClear
                    },
                    Fields = "UserEnteredFormat(TextFormat)"
                },
            };

            var updateCellsRequest = new Request()
            {
                RepeatCell = new RepeatCellRequest()
                {
                    Range = new Google.Apis.Sheets.v4.Data.GridRange()
                    {
                        SheetId = 0,
                        StartColumnIndex = 0,
                        StartRowIndex = 0,
                        EndColumnIndex = 3,
                        EndRowIndex = 500
                    },
                    Cell = new CellData()
                    {
                        UserEnteredFormat = userEnteredFormat
                    },
                    Fields = "UserEnteredFormat(TextFormat)"
                },
            };

            var updateHeaderRequest = new Request()
            {
                RepeatCell = new RepeatCellRequest()
                {
                    Range = new Google.Apis.Sheets.v4.Data.GridRange()
                    {
                        SheetId = 0,
                        StartColumnIndex = 0,
                        StartRowIndex = 0,
                        EndColumnIndex = 50,
                        EndRowIndex = 1
                    },
                    Cell = new CellData()
                    {
                        UserEnteredFormat = userEnteredFormatHeader
                    },
                    Fields = "UserEnteredFormat(TextFormat)"
                },
            };

            var updateWidthRequest = new Request()
            {
                AutoResizeDimensions = new AutoResizeDimensionsRequest()
                {
                    Dimensions = new DimensionRange
                    {
                        SheetId = 0,
                        Dimension = "COLUMNS",
                        StartIndex = 0,
                        EndIndex = 4
                    }
                }
            };

            BatchUpdateSpreadsheetRequest bussr = new BatchUpdateSpreadsheetRequest();
            bussr.Requests = new List<Request>();
            bussr.Requests.Add(clearCellsRequest);
            bussr.Requests.Add(updateCellsRequest);
            bussr.Requests.Add(updateWidthRequest);
            bussr.Requests.Add(updateHeaderRequest);
            var bur = service.Spreadsheets.BatchUpdate(bussr, spreadsheetId);
            bur.Execute();
        }
    }
}
