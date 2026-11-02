<?php
// GoldataWeb2 P0 hot-fix: this endpoint had no login check (NFR-SEC-2).
require_once __DIR__ . '/require_login.php';
require_login(['管理'], '总监');
require_write();
require_once("DBController.php");
$db_handle = new DBController();
require_once('php_functions.php');
$action = $_POST["action"];
if(!empty($action)) {
	switch($action) {
		case "add":
		    $query = "INSERT INTO lookup_tables(parent_id,ranking,table_name,table_item,value,data) VALUES('".$_POST["parent_id"]."',".$_POST["ranking"].",'".$_POST["table_name"]."','".$_POST["table_item"]."','".$_POST["value"]."','".$_POST["data"]."')";
			
		    $insert_id = $db_handle->insert($query);
		    if($insert_id){
												
					echo '	<tr class="row-tr" id="row_'.  $insert_id .'">
						<td  align="center" class="id" id="td_id_' . $insert_id .'">'.$insert_id.'</td>			
							<td  align="center" class="ranking" id="td_ranking_' . $insert_id .'">'.$_POST["ranking"].'</td>			
							<td   align="center" class="table_item" id="td_table_item_' . $insert_id .'">'.$_POST["table_item"].'</td>
							<td align="center" class="value" id="td_value_' . $insert_id .'">'.$_POST["value"].'</td>
							<td align="center" class="data" id="td_data_' . $insert_id .'">'.$_POST["data"].'</td>
							<td align="center" class="table_button" id="table_button_' . $insert_id .'">
								<button class="btnEditAction" name="edit" onClick="showEditBox(this,' . $insert_id . ')">Edit</button>
								<button class="btnDeleteAction" name="delete" onClick="callCrudAction(\'delete\',' . $insert_id . ')">Delete</button>
								<button class="btnShowChildRowsAction" name="ShowChildRows" onClick="callCrudAction(\'query_all_cats\','. $insert_id . ')">下属分类</button>
							</td>
							</tr>';
						
			}else
			{
				echo   $query;
			}
			break;
			
		case "edit":
		    $query = "UPDATE lookup_tables set ranking = '".$_POST["ranking"]."', table_item = '".$_POST["table_item"]. "', value = '".$_POST["value"].  "', data = '".$_POST["data"]. "'  WHERE  id=".$_POST["row_id"];
		
			
		    $result = $db_handle->execute($query);
			if($result){
				  
				  echo "succues ".$query ;
			}
			break;			
		
		case "delete": 
		    if(!empty($_POST["row_id"])) 
			{
				
				 $query = "SELECT count(*) as count FROM lookup_tables WHERE parent_id='".$_POST["row_id"]."'";
				//echo  $query;
				$main_tables = $db_handle->runQuery( $query);
				if(isset($main_tables[0]["count"]))
				{
					if( $main_tables[0]["count"]>0)
					{
						echo "-1";
					}
					else
					{
						$query = "DELETE FROM lookup_tables WHERE id='".$_POST["row_id"]."'";
						$result = $db_handle->execute($query);
						echo '1';
					}
				} 
				else
				{
					echo '0';
				}
			
			}
			
		break;	
		case "query_all_cats":
		   
			
			 echo '<table id="cat_table" border="1"  cellpadding="2" cellspacing="0">';
			
			$query = "SELECT parent_id as parent_parent_id ,table_item as child_table_name ,table_name as parent_table_name FROM lookup_tables where id='".$_POST["row_id"]."'";
			//$query = "SELECT child.parent_id as parent_parent_id , parent.table_item as child_table_name  , parent.table_name as parent_table_name FROM lookup_tables child left join lookup_tables parent on child.parent_id = parent.id where child.id='".$_POST["row_id"]."'";
			//echo  $query;
			$parent_tables = $db_handle->runQuery( $query);
			$parent_parent_id=$parent_tables[0]["parent_parent_id"];
			echo 'parent_parent_id '. $parent_parent_id .' : parent_id '.$_POST["row_id"];
			if($parent_parent_id>0 || $_POST["row_id"]>0)
			{echo '	<tr>
				<td colspan="6" align="left">
					<button class="btnShowCatRowsAction" name="ShowCatRows" onClick="callCrudAction(\'query_all_cats\','. $parent_parent_id.')">返回 '.$parent_tables[0]["parent_table_name"].'</button>
				</td>
			</tr>';
			}		
				
			
				
			 $query = "SELECT child.value as value,child.data as data, child.id as id, child.table_name as table_name,child.table_item as table_item,child.ranking as ranking,child.parent_id as parent_id,parent.parent_id as parent_parent_id FROM lookup_tables child left join lookup_tables parent on child.parent_id = parent.id where child.parent_id='".$_POST["row_id"]."' order by child.ranking";
			//echo  $query;
			$main_tables = $db_handle->runQuery( $query);
			
			echo '<tr><td colspan="6"  class="table_name" id="main_table_name" align="center" >';
						if(isset($parent_tables[0]["child_table_name"]) && $parent_tables[0]["child_table_name"]!='')
						{
							echo 	$parent_tables[0]["child_table_name"];
						}
						else
						{
							echo 	'主要表单名称';
						}
			echo '	</td></tr>';
				
			  echo ' <tr><td align="center">id</td align="center"><td align="center">排序</td><td align="center" >表单明细</td> <td align="center">Value</td><td align="center">Data</td><td align="center">操作</td></tr>
				';
		   if(!empty($main_tables)) 
		   {
			
				foreach($main_tables as $k=>$v) 
				{
					
							echo '<tr align="center" class="row-tr" id="row_'.$main_tables[$k]["id"].'">
							<td align="center" class="table_id" id="td_table_id_'.$main_tables[$k]["id"].'" >
							'. $main_tables[$k]["id"].'
							</td>
							<td  align="center" class="ranking" id="td_ranking_'.$main_tables[$k]["id"].'" >
							'. $main_tables[$k]["ranking"].'
							</td>
							
							<td align="center" class="table_item" id="td_table_item_'. $main_tables[$k]["id"].'">
							'. $main_tables[$k]["table_item"].'
							</td>
							<td align="center" class="value" id="td_value_'. $main_tables[$k]["id"].'">
							'. $main_tables[$k]["value"].'
							</td>
							<td align="center" class="data" id="td_data_'. $main_tables[$k]["id"].'">
							'. $main_tables[$k]["data"].'
							</td>
							<td class="table_button" id="table_button_'.$main_tables[$k]["id"].'">
							<button class="btnEditAction" name="edit" onClick="showEditBox(this,'.$main_tables[$k]["id"].')">编辑</button>
							<button class="btnDeleteAction" name="delete" onClick="callCrudAction(\'delete\','. $main_tables[$k]["id"].')">删除</button>';
							
							echo '<button class="btnShowChildRowsAction" name="ShowChildRows" onClick="callCrudAction(\'query_all_cats\','. $main_tables[$k]["id"].')">下属分类</button>';
							
							echo '</td>
							</tr>
							
							';
			
				}
				
			}echo '
				<tr><td></td>
						<td align="center"><input class="input" type="text" id="input_ranking" value="" /></td>
						<td  align="center" ><input  class="input" type="text" id="input_table_item" value="" /></td>
						<td align="center" ><input  class="input" type="text" id="input_value" value="" /></td>
						<td align="center" ><input  class="input" type="text" id="input_data" value="" /></td>
						<td  align="center" ><button id="btnAddAction" name="submit" onClick="callCrudAction(\'add\','. $_POST["row_id"].')">Add</button></td></tr>
				</table>';
			break;
			
}
}
?>