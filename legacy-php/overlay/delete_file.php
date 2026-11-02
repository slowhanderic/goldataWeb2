<?php // GoldataWeb2 P0 hot-fix: this page had no login check (NFR-SEC-2).
require_once __DIR__ . '/require_login.php';
require_login();
require_write(); ?>
<script  src="jsfunctions.js" type="text/javascript"></script>
<!-----------------------------------------------------------20230112 may be using old -->


<script language="javascript" >
if(deletealert()==false)
{
window.history.back();
}
</script>
<?php
include('connectDB.php');

if (isset($_GET['id']) && is_numeric($_GET['id']) && isset($_GET['table_name']) && isset($_GET['calling_page']) && isset($_GET['calling_page_rowid']))
{
		$id = $_GET['id'];
		$table_name = $_GET['table_name'];
		$calling_page =$_GET['calling_page'];
		$calling_page_rowid =$_GET['calling_page_rowid'];
		?>
		
		 <form action="" method="post">
              <input    type="radio"  name="choice" value="YES"> <label>删除 <?php echo $table_name .'='.$id ?></label><br>
              <input    type="radio"  name="choice"  value="NO"> <label>不删除</label><br>
            <input type="submit" name="submit" value="确认">
		</form>
        
		
		
        
  <?php      	$result = mysqli_query($conn,"SELECT * FROM file_path WHERE id=$id")
		or die(mysqli_error());
		$row = mysqli_fetch_array($result);
	
		if($row)
		{
			$id = $row['id'];
			$file_path= $row['file_path'];
		}
		
	if (isset($_POST['choice']) &&  $_POST['choice'] =="YES")
		{
				$sql ="DELETE FROM file_path WHERE id='" .$id."'";
				echo "Record deleted successfully";
				if ($conn->query($sql) === TRUE) {
				echo "Record deleted successfully";
				
				//rename($file_path,dirname($file_path)."/删除-".basename($file_path));
				unlink($file_path);
				} else {
					echo "Error: " . $sql . "<br>" . $conn->error;
				}
				
				echo $sql;
			header("Location: ". $calling_page.".php?id=". $calling_page_rowid  );
		}else if (isset($_POST['choice']) &&  $_POST['choice'] =="NO")
		{
				header("Location: ". $calling_page.".php?id=". $calling_page_rowid  );
		}
		
}
else
{
		header("Location: ". $calling_page.".php?id=". $calling_page_rowid  );
}
?>
