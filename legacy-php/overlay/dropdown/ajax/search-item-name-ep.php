<?php
namespace Goldata;

use Goldata\itemNameSearch;
// GoldataWeb2 P0 hot-fix: this endpoint had no login check (NFR-SEC-2).
require_once __DIR__ . '/../../require_login.php';
\require_login();
if (! empty($_POST["search_name"])) {
    
    $searchname = $_POST["search_name"];
	
    
   require_once __DIR__ . '/../Model/ItemNameSearch.php';
   $search_name= new itemNameSearch();

   $search_nameResult = $search_name->itemSearchByName($searchname);

 if(isset($search_nameResult))
 {
    foreach ($search_nameResult as $search_name) 
	{	
		
		 $return_arr[] = array(	"pcat" => $search_name["pcat"],
								"psubcat" => $search_name["psubcat"],
								"pitem" => $search_name["pitem"],
								"ptype" => $search_name["ptype"],
								"pcatid" => $search_name["pcatid"],
								"psubcatid" => $search_name["psubcatid"],
								"pitemid" => $search_name["pitemid"],
								"ptypeid" => $search_name["ptypeid"]
								 
								);

    }
 }else	
	{ $return_arr[] = array();}
	echo json_encode($return_arr);
}
?>